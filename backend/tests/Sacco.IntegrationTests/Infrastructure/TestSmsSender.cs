using System.Collections.Concurrent;
using Sacco.Shared.Notifications;

namespace Sacco.IntegrationTests.Infrastructure;

/// <summary>Captures every SMS a test run sends instead of calling a real gateway, so a test can read
/// back an OTP code the same way a member would read it off their phone.</summary>
public sealed class TestSmsSender : ISmsSender
{
    public string Name => "Test capture";
    public bool IsSandbox => true;

    public ConcurrentBag<(string PhoneNumber, string Body)> Sent { get; } = [];

    public Task<string?> SendAsync(string phoneNumber, string body, CancellationToken ct)
    {
        Sent.Add((phoneNumber, body));
        return Task.FromResult<string?>($"TEST-SMS-{Guid.NewGuid():N}"[..16]);
    }

    /// <summary>The 6-digit OTP from the most recently sent message to this phone number.</summary>
    public string LastCodeFor(string phoneNumber)
    {
        var message = Sent.Last(m => m.PhoneNumber == phoneNumber);
        var match = System.Text.RegularExpressions.Regex.Match(message.Body, @"\b(\d{6})\b");
        return match.Success ? match.Groups[1].Value : throw new InvalidOperationException($"No 6-digit code found in: {message.Body}");
    }
}
