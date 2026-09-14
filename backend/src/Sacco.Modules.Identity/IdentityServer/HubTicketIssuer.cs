using System.Security.Claims;
using Open.IdentityServer;
using Sacco.Shared.Auth;
using Sacco.Shared.Time;

namespace Sacco.Modules.Identity.IdentityServer;

/// <summary>
/// Mints hub tickets with the IdentityServer signing key. The audience is what keeps a ticket out of
/// the REST API: the bearer scheme validates <c>aud=sacco-api</c>, the hub scheme <c>aud=sacco-hub</c>.
/// </summary>
public sealed class HubTicketIssuer(IdentityServerTools tools, IdentityServerSettings settings, IClock clock) : IHubTicketIssuer
{
    public async Task<HubTicket> IssueAsync(Guid userId, string userName, Guid tenantId, string tenantSlug, CancellationToken ct)
    {
        var lifetime = (int)HubTicketAuth.Lifetime.TotalSeconds;
        var token = await tools.IssueJwtAsync(lifetime, settings.IssuerUri,
        [
            new Claim("sub", userId.ToString()),
            new Claim("name", userName),
            new Claim("tenant", tenantSlug),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("aud", HubTicketAuth.Audience),
            new Claim("scope", HubTicketAuth.Audience),
        ]);
        return new HubTicket(token, clock.UtcNow.AddSeconds(lifetime));
    }
}
