using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Sacco.Modules.Members.Application;

public sealed class TurnstileOptions
{
    public const string SectionName = "Turnstile";
    /// <summary>Cloudflare's always-pass test secret is the default so demos need no real key.</summary>
    public string SecretKey { get; set; } = "1x0000000000000000000000000000000AA";
    public string VerifyUrl { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
    /// <summary>When true (default outside Production), tokens are accepted without calling Cloudflare.</summary>
    public bool Sandbox { get; set; } = true;
}

/// <summary>Server-side Turnstile check — defence in depth behind the public site's own siteverify call.</summary>
public interface ITurnstileVerifier
{
    Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken ct);
}

public sealed class TurnstileVerifier(IHttpClientFactory httpClientFactory, IOptions<TurnstileOptions> options) : ITurnstileVerifier
{
    public async Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (o.Sandbox) return !token.StartsWith("fail", StringComparison.OrdinalIgnoreCase); // "fail-..." simulates a bot in demos/tests

        var client = httpClientFactory.CreateClient(nameof(TurnstileVerifier));
        var form = new Dictionary<string, string> { ["secret"] = o.SecretKey, ["response"] = token };
        if (!string.IsNullOrEmpty(remoteIp)) form["remoteip"] = remoteIp;
        using var response = await client.PostAsync(o.VerifyUrl, new FormUrlEncodedContent(form), ct);
        if (!response.IsSuccessStatusCode) return false;
        var body = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(ct);
        return body?.Success == true;
    }

    private sealed record SiteVerifyResponse(bool Success);
}
