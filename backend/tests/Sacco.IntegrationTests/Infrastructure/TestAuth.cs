using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sacco.Shared.Auth;

namespace Sacco.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only authentication: the caller declares who they are and which permissions they hold
/// via headers. This exercises the real permission policy pipeline with a stub token source.
/// </summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string PermissionsHeader = "X-Test-Permissions";
    public const string BranchHeader = "X-Test-Branch";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var userId) || string.IsNullOrWhiteSpace(userId))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new("sub", userId!), new("name", $"test-{userId}") };
        if (Request.Headers.TryGetValue(BranchHeader, out var branch) && !string.IsNullOrWhiteSpace(branch))
            claims.Add(new Claim("branch_id", branch!));
        if (Request.Headers.TryGetValue(PermissionsHeader, out var perms))
            claims.AddRange(perms.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => new Claim("permission", p)));

        var identity = new ClaimsIdentity(claims, SchemeName, "name", "role");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>Resolves permissions from the test principal's claims (replaced by the Identity module in the real host).</summary>
public sealed class ClaimsPermissionResolver(IHttpContextAccessor accessor) : IPermissionResolver
{
    public Task<IReadOnlySet<string>> GetPermissionsAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var set = accessor.HttpContext?.User.FindAll("permission").Select(c => c.Value).ToHashSet() ?? [];
        return Task.FromResult<IReadOnlySet<string>>(set);
    }
}
