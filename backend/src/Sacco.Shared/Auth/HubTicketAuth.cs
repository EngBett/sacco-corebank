namespace Sacco.Shared.Auth;

/// <summary>
/// Real-time (SignalR) connections cannot carry the portal's bearer token: the BFF keeps API tokens
/// server-side and browsers cannot set headers on WebSocket upgrades. Instead the API mints a short-lived
/// <em>hub ticket</em> — a JWT signed with the same key material but with its own audience — that is
/// only accepted by the hub's authentication scheme, never by the REST API. The ticket is passed in the
/// <c>access_token</c> query parameter per the SignalR convention.
/// </summary>
public static class HubTicketAuth
{
    public const string Scheme = "HubTicket";
    public const string Audience = "sacco-hub";
    public const string PathPrefix = "/hubs";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
}

public interface IHubTicketIssuer
{
    /// <summary>Issues a hub ticket for the given user; the caller has already authenticated them.</summary>
    Task<HubTicket> IssueAsync(Guid userId, string userName, Guid tenantId, string tenantSlug, CancellationToken ct);
}

public sealed record HubTicket(string Token, DateTimeOffset ExpiresAt);
