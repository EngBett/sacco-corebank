using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Sacco.Shared.Notifications;

namespace Sacco.IntegrationTests.Infrastructure;

/// <summary>Captures every email this factory's app sends — follow an activation or reset link the way a user would.</summary>
public sealed class TestEmailSender : IEmailSender
{
    public string Name => "Test capture";
    public bool IsSandbox => true;

    public ConcurrentQueue<(string To, string Subject, string Html)> Sent { get; } = [];

    public Task<string?> SendAsync(string to, string subject, string bodyHtml, IReadOnlyList<EmailAttachment>? attachments, CancellationToken ct)
    {
        Sent.Enqueue((to, subject, bodyHtml));
        return Task.FromResult<string?>($"TEST-MAIL-{Guid.NewGuid():N}"[..17]);
    }

    /// <summary>The path and query of the first /account link in the newest email to <paramref name="to"/>, e.g. "/account/activate?tenant=demo&amp;token=…".</summary>
    public string LastLinkFor(string to, string page)
    {
        var message = Sent.Last(m => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase));
        var match = Regex.Match(message.Html, $@"href=""[^""]*?(/account/{Regex.Escape(page)}\?[^""]+)""");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : throw new InvalidOperationException($"No /account/{page} link in '{message.Subject}'.");
    }

    public int CountFor(string to) => Sent.Count(m => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase));
}
