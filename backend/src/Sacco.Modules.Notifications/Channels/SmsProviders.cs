using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Sacco.Modules.Notifications.Channels;

/// <summary>
/// Every sender in this file implements the same <see cref="ISmsSender"/> contract used by <c>AfricasTalkingSmsSender</c>;
/// <c>NotificationsModule</c> wires up exactly one of them from <c>Notifications:Sms:Provider</c>. Swapping a SACCO from one
/// gateway to another — Africa's Talking today, Twilio or a direct Safaricom/Airtel contract tomorrow — is a configuration
/// change, never a code change, and every domain workflow (<c>OutboundDispatcher</c> included) only ever sees <see cref="ISmsSender"/>.
/// </summary>

/// <summary>Twilio Programmable Messaging: https://www.twilio.com/docs/messaging/api/message-resource. Basic auth (Account SID : Auth Token), form-encoded body.</summary>
public sealed class TwilioSmsSender(HttpClient http, IOptions<NotificationChannelSettings> options) : ISmsSender
{
    public string Name => "Twilio";
    public bool IsSandbox => false;

    public async Task<string?> SendAsync(string phoneNumber, string body, CancellationToken ct)
    {
        var s = options.Value.Sms.Twilio;
        if (string.IsNullOrEmpty(s.AccountSid) || string.IsNullOrEmpty(s.AuthToken))
            throw new InvalidOperationException("Notifications:Sms:Twilio:AccountSid/AuthToken are not configured.");
        if (string.IsNullOrEmpty(s.From) && string.IsNullOrEmpty(s.MessagingServiceSid))
            throw new InvalidOperationException("Notifications:Sms:Twilio:From or MessagingServiceSid is required.");

        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(s.BaseUrl), $"2010-04-01/Accounts/{s.AccountSid}/Messages.json"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{s.AccountSid}:{s.AuthToken}")));
        var form = new Dictionary<string, string> { ["To"] = "+" + phoneNumber.TrimStart('+'), ["Body"] = body };
        if (!string.IsNullOrEmpty(s.MessagingServiceSid)) form["MessagingServiceSid"] = s.MessagingServiceSid; else form["From"] = s.From!;
        req.Content = new FormUrlEncodedContent(form);
        using var res = await http.SendAsync(req, ct);
        var payload = await res.Content.ReadFromJsonAsync<TwilioResponse>(cancellationToken: ct);
        if (!res.IsSuccessStatusCode || payload?.Sid is null)
            throw new InvalidOperationException($"Twilio rejected the message: {(int)res.StatusCode} {payload?.Message ?? res.ReasonPhrase}");
        return payload.Sid;
    }

    private sealed record TwilioResponse(string? Sid, string? Status, string? Message, [property: JsonPropertyName("error_code")] int? ErrorCode);
}

/// <summary>Meta WhatsApp Cloud API: https://developers.facebook.com/docs/whatsapp/cloud-api/reference/messages. Bearer token, JSON body, text message.</summary>
public sealed class WhatsAppCloudApiSmsSender(HttpClient http, IOptions<NotificationChannelSettings> options) : ISmsSender
{
    public string Name => "WhatsApp";
    public bool IsSandbox => false;

    public async Task<string?> SendAsync(string phoneNumber, string body, CancellationToken ct)
    {
        var s = options.Value.Sms.WhatsApp;
        if (string.IsNullOrEmpty(s.PhoneNumberId) || string.IsNullOrEmpty(s.AccessToken))
            throw new InvalidOperationException("Notifications:Sms:WhatsApp:PhoneNumberId/AccessToken are not configured.");
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(s.BaseUrl), $"{s.PhoneNumberId}/messages"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.AccessToken);
        req.Content = JsonContent.Create(new { messaging_product = "whatsapp", to = phoneNumber.TrimStart('+'), type = "text", text = new { body, preview_url = false } });
        using var res = await http.SendAsync(req, ct);
        var payload = await res.Content.ReadFromJsonAsync<WhatsAppResponse>(cancellationToken: ct);
        var messageId = payload?.Messages?.FirstOrDefault()?.Id;
        if (!res.IsSuccessStatusCode || messageId is null)
            throw new InvalidOperationException($"WhatsApp rejected the message: {(int)res.StatusCode} {payload?.Error?.Message ?? res.ReasonPhrase}");
        return messageId;
    }

    private sealed record WhatsAppResponse(List<WhatsAppMessage>? Messages, WhatsAppError? Error);
    private sealed record WhatsAppMessage(string? Id);
    private sealed record WhatsAppError(string? Message);
}

/// <summary>
/// Safaricom enterprise Bulk SMS. Safaricom does not publish this the way it publishes Daraja (no open sandbox); this
/// mirrors Daraja's own OAuth2 client-credentials + Bearer shape as a reasonable starting point. Confirm the actual
/// endpoint path and payload against the SACCO's Safaricom Bulk SMS contract before go-live.
/// </summary>
public sealed class SafaricomSmsSender(HttpClient http, IOptions<NotificationChannelSettings> options) : ISmsSender
{
    public string Name => "Safaricom";
    public bool IsSandbox => false;
    private SafaricomSmsSettings S => options.Value.Sms.Safaricom;

    public async Task<string?> SendAsync(string phoneNumber, string body, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(S.BaseUrl)) throw new InvalidOperationException("Notifications:Sms:Safaricom:BaseUrl is required in Live mode.");
        if (string.IsNullOrEmpty(S.ClientId) || string.IsNullOrEmpty(S.ClientSecret)) throw new InvalidOperationException("Notifications:Sms:Safaricom:ClientId/ClientSecret are not configured.");

        var baseUri = new Uri(S.BaseUrl);
        var basic = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{S.ClientId}:{S.ClientSecret}"));
        using var tokenReq = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "oauth/v1/generate?grant_type=client_credentials"));
        tokenReq.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        using var tokenRes = await http.SendAsync(tokenReq, ct);
        var token = (await tokenRes.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("access_token").GetString();

        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "sms/v1/send"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = JsonContent.Create(new { senderId = S.SenderId, recipient = phoneNumber, message = body });
        using var res = await http.SendAsync(req, ct);
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var messageId = doc.TryGetProperty("messageId", out var m) ? m.GetString() : null;
        if (!res.IsSuccessStatusCode || messageId is null)
            throw new InvalidOperationException($"Safaricom Bulk SMS rejected the message: {(int)res.StatusCode} {(doc.TryGetProperty("errorMessage", out var e) ? e.GetString() : res.ReasonPhrase)}");
        return messageId;
    }
}

/// <summary>
/// Airtel Africa business messaging. Modelled on the already-verified <c>AirtelMoneyProvider</c>'s OAuth2
/// client-credentials + <c>X-Country</c>/<c>X-Currency</c> header shape. Confirm the SMS-specific endpoint path and
/// payload against Airtel's contract before go-live.
/// </summary>
public sealed class AirtelSmsSender(HttpClient http, IOptions<NotificationChannelSettings> options) : ISmsSender
{
    public string Name => "Airtel";
    public bool IsSandbox => false;
    private AirtelSmsSettings S => options.Value.Sms.Airtel;

    public async Task<string?> SendAsync(string phoneNumber, string body, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(S.BaseUrl)) throw new InvalidOperationException("Notifications:Sms:Airtel:BaseUrl is required in Live mode.");
        if (string.IsNullOrEmpty(S.ClientId) || string.IsNullOrEmpty(S.ClientSecret)) throw new InvalidOperationException("Notifications:Sms:Airtel:ClientId/ClientSecret are not configured.");

        var baseUri = new Uri(S.BaseUrl);
        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "auth/oauth2/token"));
        tokenReq.Content = JsonContent.Create(new { client_id = S.ClientId, client_secret = S.ClientSecret, grant_type = "client_credentials" });
        using var tokenRes = await http.SendAsync(tokenReq, ct);
        var token = (await tokenRes.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct)).GetProperty("access_token").GetString();

        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "standard/v1/messaging"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("X-Country", S.Country);
        req.Content = JsonContent.Create(new { senderId = S.SenderId, recipient = "+" + phoneNumber.TrimStart('+'), message = body });
        using var res = await http.SendAsync(req, ct);
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var messageId = doc.TryGetProperty("data", out var d) && d.TryGetProperty("messageId", out var m) ? m.GetString() : null;
        if (!res.IsSuccessStatusCode || messageId is null)
            throw new InvalidOperationException($"Airtel messaging rejected the message: {(int)res.StatusCode} {(doc.TryGetProperty("status", out var st) && st.TryGetProperty("message", out var sm) ? sm.GetString() : res.ReasonPhrase)}");
        return messageId;
    }
}

/// <summary>
/// The local mock SMS gateway under <c>mocked-providers/mocked-sms-server</c>: a generic REST inbox with a small web UI
/// (open its <see cref="MockSmsSettings.BaseUrl"/> in a browser) so local dev and demos show real "sent" SMS without any
/// vendor credentials — the SMS equivalent of pointing <c>Notifications:Email:Smtp</c> at Mailpit. Point any Live
/// deployment at it by setting <c>Notifications:Sms:Provider=Mock</c>.
/// </summary>
public sealed class MockSmsSender(HttpClient http, IOptions<NotificationChannelSettings> options) : ISmsSender
{
    public string Name => "Mock SMS gateway";
    public bool IsSandbox => false;

    public async Task<string?> SendAsync(string phoneNumber, string body, CancellationToken ct)
    {
        var s = options.Value.Sms.Mock;
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(s.BaseUrl), "sms/send"));
        req.Content = JsonContent.Create(new { to = phoneNumber, message = body });
        using var res = await http.SendAsync(req, ct);
        var payload = await res.Content.ReadFromJsonAsync<MockResponse>(cancellationToken: ct);
        if (!res.IsSuccessStatusCode || payload?.MessageId is null)
            throw new InvalidOperationException($"Mock SMS gateway rejected the message: {(int)res.StatusCode} {payload?.Error ?? res.ReasonPhrase}");
        return payload.MessageId;
    }

    private sealed record MockResponse(string? MessageId, string? Status, string? Error);
}
