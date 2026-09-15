using System.Net.Http.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Sacco.Modules.Notifications.Domain;

namespace Sacco.Modules.Notifications.Channels;

public sealed class NotificationChannelSettings
{
    public const string SectionName = "Notifications";
    public SmsSettings Sms { get; set; } = new();
    public EmailSettings Email { get; set; } = new();
    public DeliverySettings Delivery { get; set; } = new();
    /// <summary>StackExchange.Redis connection string for the SignalR backplane. Null = single instance, in-process.</summary>
    public string? Redis { get; set; }
}

/// <summary>Names <see cref="SmsSettings.Provider"/> can take. Adding a gateway is: implement <see cref="ISmsSender"/>, add its settings block, add one case to <c>NotificationsModule.RegisterLiveSmsSender</c> — nothing else in the system knows which gateway is behind the interface.</summary>
public static class SmsProviderNames
{
    public const string AfricasTalking = "AfricasTalking";
    public const string Twilio = "Twilio";
    public const string WhatsApp = "WhatsApp";
    public const string Safaricom = "Safaricom";
    public const string Airtel = "Airtel";
    /// <summary>The local mock SMS gateway under <c>mocked-providers/mocked-sms-server</c> — the SMS equivalent of pointing <c>Notifications:Email:Smtp</c> at Mailpit.</summary>
    public const string Mock = "Mock";
}

public sealed class SmsSettings
{
    public string Mode { get; set; } = "Sandbox";
    /// <summary>Which gateway to use when <see cref="Mode"/> is <c>Live</c>. One of <see cref="SmsProviderNames"/>. Every downstream caller only ever sees <see cref="ISmsSender"/>.</summary>
    public string Provider { get; set; } = SmsProviderNames.AfricasTalking;
    public AfricasTalkingSettings AfricasTalking { get; set; } = new();
    public TwilioSettings Twilio { get; set; } = new();
    public WhatsAppSettings WhatsApp { get; set; } = new();
    public SafaricomSmsSettings Safaricom { get; set; } = new();
    public AirtelSmsSettings Airtel { get; set; } = new();
    public MockSmsSettings Mock { get; set; } = new();
}
public sealed class AfricasTalkingSettings
{
    public string BaseUrl { get; set; } = "https://api.africastalking.com/";
    public string? Username { get; set; }
    public string? ApiKey { get; set; }
    public string? SenderId { get; set; }
}
/// <summary>Twilio Programmable Messaging (also fronts Twilio's own WhatsApp channel if <see cref="MessagingServiceSid"/>/<see cref="From"/> is a WhatsApp-enabled sender).</summary>
public sealed class TwilioSettings
{
    public string BaseUrl { get; set; } = "https://api.twilio.com/";
    public string? AccountSid { get; set; }
    public string? AuthToken { get; set; }
    /// <summary>E.164 sender number. Either this or <see cref="MessagingServiceSid"/> is required.</summary>
    public string? From { get; set; }
    public string? MessagingServiceSid { get; set; }
}
/// <summary>Meta WhatsApp Cloud API — sends the notification body as a free-form text message. The member must have messaged the SACCO's WhatsApp number in the last 24h, or this must be a pre-approved template (out of scope for the generic notification body); confirm the SACCO's opted-in audience before enabling in production.</summary>
public sealed class WhatsAppSettings
{
    public string BaseUrl { get; set; } = "https://graph.facebook.com/v20.0/";
    public string? PhoneNumberId { get; set; }
    public string? AccessToken { get; set; }
}
/// <summary>Safaricom enterprise Bulk SMS. Safaricom issues this per commercial contract (no public sandbox like Daraja); endpoint path and payload below are a starter shape — confirm against the SACCO's actual contract before go-live, the same way DarajaMpesaProvider was confirmed against the Daraja docs.</summary>
public sealed class SafaricomSmsSettings
{
    public string? BaseUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? SenderId { get; set; }
}
/// <summary>Airtel Africa business messaging. Modelled on the same OAuth2 client-credentials + country-header shape as the verified <c>AirtelMoneyProvider</c>; confirm the SMS-specific endpoint against Airtel's contract before go-live.</summary>
public sealed class AirtelSmsSettings
{
    public string? BaseUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string Country { get; set; } = "KE";
    public string? SenderId { get; set; }
}
/// <summary>The local mock SMS gateway (<c>mocked-providers/mocked-sms-server</c>): a generic REST inbox with a web UI, so demos and local dev show real "sent" SMS without any vendor credentials — the SMS equivalent of Mailpit.</summary>
public sealed class MockSmsSettings
{
    public string BaseUrl { get; set; } = "http://localhost:5108/";
}
public sealed class EmailSettings
{
    public string Mode { get; set; } = "Sandbox";
    public SmtpSettings Smtp { get; set; } = new();
}
public sealed class SmtpSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "no-reply@sacco.example";
    public string FromName { get; set; } = "SACCO Platform";
    public bool UseStartTls { get; set; } = true;
}
public sealed class DeliverySettings
{
    /// <summary>Every notification is emailed when the user has an email address.</summary>
    public bool EmailEnabled { get; set; } = true;
    /// <summary>Notification kinds (prefix match) that also go out by SMS: the ones that need someone's attention now.</summary>
    public List<string> SmsKindPrefixes { get; set; } = ["ledger.journal.pending", "savings.withdrawal.pending", "loans.pending_approval", "loans.ready_to_disburse", "loans.writeoff.pending", "loans.restructure.pending", "members.exit.pending", "payments.failed", "savings.withdrawal.payout_failed"];
}

public interface ISmsSender
{
    string Name { get; }
    bool IsSandbox { get; }
    Task<string?> SendAsync(string phoneNumber, string body, CancellationToken ct);
}

public interface IEmailSender
{
    string Name { get; }
    bool IsSandbox { get; }
    Task<string?> SendAsync(string to, string subject, string body, CancellationToken ct);
}

/// <summary>No gateway: the outbox row is the delivery. Returns a deterministic-looking reference so demos read like the real thing.</summary>
public sealed class SandboxSmsSender : ISmsSender
{
    public string Name => "Sandbox SMS";
    public bool IsSandbox => true;
    public Task<string?> SendAsync(string phoneNumber, string body, CancellationToken ct) => Task.FromResult<string?>($"SBX-SMS-{Guid.NewGuid():N}"[..20]);
}

public sealed class SandboxEmailSender : IEmailSender
{
    public string Name => "Sandbox email";
    public bool IsSandbox => true;
    public Task<string?> SendAsync(string to, string subject, string body, CancellationToken ct) => Task.FromResult<string?>($"SBX-MAIL-{Guid.NewGuid():N}"[..21]);
}

/// <summary>Africa's Talking bulk SMS (the gateway most Kenyan SACCOs already use). Written to the published REST API; verify against the AT sandbox before go-live.</summary>
public sealed class AfricasTalkingSmsSender(HttpClient http, IOptions<NotificationChannelSettings> options) : ISmsSender
{
    public string Name => "Africa's Talking";
    public bool IsSandbox => false;

    public async Task<string?> SendAsync(string phoneNumber, string body, CancellationToken ct)
    {
        var s = options.Value.Sms.AfricasTalking;
        if (string.IsNullOrEmpty(s.Username) || string.IsNullOrEmpty(s.ApiKey)) throw new InvalidOperationException("Notifications:Sms:AfricasTalking:Username/ApiKey are not configured.");
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(s.BaseUrl), "version1/messaging"));
        req.Headers.Add("apiKey", s.ApiKey);
        req.Headers.Accept.ParseAdd("application/json");
        var form = new Dictionary<string, string> { ["username"] = s.Username, ["to"] = "+" + phoneNumber.TrimStart('+'), ["message"] = body };
        if (!string.IsNullOrEmpty(s.SenderId)) form["from"] = s.SenderId;
        req.Content = new FormUrlEncodedContent(form);
        using var res = await http.SendAsync(req, ct);
        var payload = await res.Content.ReadFromJsonAsync<AtResponse>(cancellationToken: ct);
        var recipient = payload?.SMSMessageData?.Recipients?.FirstOrDefault();
        if (!res.IsSuccessStatusCode || recipient is null || recipient.StatusCode >= 400)
            throw new InvalidOperationException($"Africa's Talking rejected the message: {(int)res.StatusCode} {recipient?.Status ?? payload?.SMSMessageData?.Message}");
        return recipient.MessageId;
    }

    private sealed record AtResponse(AtData? SMSMessageData);
    private sealed record AtData(string? Message, List<AtRecipient>? Recipients);
    private sealed record AtRecipient(string? Number, string? Status, int StatusCode, string? MessageId);
}

/// <summary>SMTP via MailKit (MIT). Any provider with SMTP credentials: SES, SendGrid, a SACCO's own mail server.</summary>
public sealed class SmtpEmailSender(IOptions<NotificationChannelSettings> options) : IEmailSender
{
    public string Name => "SMTP";
    public bool IsSandbox => false;

    public async Task<string?> SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var s = options.Value.Email.Smtp;
        if (string.IsNullOrEmpty(s.Host)) throw new InvalidOperationException("Notifications:Email:Smtp:Host is not configured.");
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(s.FromName, s.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };
        using var client = new SmtpClient();
        await client.ConnectAsync(s.Host, s.Port, s.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, ct);
        if (!string.IsNullOrEmpty(s.Username)) await client.AuthenticateAsync(s.Username, s.Password, ct);
        var response = await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
        return message.MessageId ?? response;
    }
}
