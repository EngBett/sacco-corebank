using Open.IdentityServer;
using Open.IdentityServer.Models;

namespace Sacco.Modules.Identity.IdentityServer;

public sealed class IdentityServerSettings
{
    public const string SectionName = "IdentityServer";
    /// <summary>Issuer URL; must match Authentication:Authority on the API side.</summary>
    public string IssuerUri { get; set; } = "http://localhost:5000";
    /// <summary>"Developer" (auto-generated key file) or "Certificate" (PFX from secrets manager). Never Developer in production.</summary>
    public string SigningCredential { get; set; } = "Developer";
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }
    /// <summary>Base64 PFX, for secrets managers that can't mount files.</summary>
    public string? CertificateBase64 { get; set; }
    public List<ClientSettings> Clients { get; set; } = [];
}

public sealed class ClientSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    /// <summary>"code" (authorization code + PKCE), "password" (resource-owner, first-party demo/CLI only), "client_credentials".</summary>
    public List<string> GrantTypes { get; set; } = [];
    public string? ClientSecret { get; set; }
    public bool RequireClientSecret { get; set; } = true;
    public List<string> RedirectUris { get; set; } = [];
    public List<string> PostLogoutRedirectUris { get; set; } = [];
    public List<string> AllowedCorsOrigins { get; set; } = [];
    public bool AllowOfflineAccess { get; set; } = true;
    public int AccessTokenLifetimeSeconds { get; set; } = 900;
}

public static class IdentityServerConfig
{
    public const string ApiScopeName = "sacco-api";
    public const string TenantClaim = "tenant";

    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
        new IdentityResource("tenant", "SACCO tenant", [TenantClaim]),
    ];

    public static IEnumerable<ApiScope> ApiScopes => [new ApiScope(ApiScopeName, "SACCO Platform API")];

    public static IEnumerable<ApiResource> ApiResources =>
    [
        new ApiResource(ApiScopeName, "SACCO Platform API")
        {
            Scopes = { ApiScopeName },
            UserClaims = { "name", "email", "preferred_username", "role", TenantClaim, SubjectClaims.MemberIdClaim, SubjectClaims.BranchIdClaim },
        },
    ];

    public static IEnumerable<Client> Clients(IdentityServerSettings settings) => settings.Clients.Select(c =>
    {
        var grantTypes = (c.GrantTypes.Count == 0 ? ["code"] : c.GrantTypes.Distinct()).Select(g => g switch
        {
            "code" => OidcConstants.GrantTypes.AuthorizationCode,
            "password" => OidcConstants.GrantTypes.Password,
            "client_credentials" => OidcConstants.GrantTypes.ClientCredentials,
            _ => throw new InvalidOperationException($"Unsupported grant type '{g}' for client {c.ClientId}."),
        }).ToList();

        var client = new Client
        {
            ClientId = c.ClientId,
            ClientName = c.ClientName,
            AllowedGrantTypes = grantTypes,
            RequireClientSecret = c.RequireClientSecret,
            RequirePkce = grantTypes.Contains(OidcConstants.GrantTypes.AuthorizationCode),
            RedirectUris = c.RedirectUris,
            PostLogoutRedirectUris = c.PostLogoutRedirectUris,
            AllowedCorsOrigins = c.AllowedCorsOrigins,
            AllowOfflineAccess = c.AllowOfflineAccess,
            AllowedScopes = { "openid", "profile", "tenant", ApiScopeName },
            AccessTokenLifetime = c.AccessTokenLifetimeSeconds,
            RefreshTokenUsage = TokenUsage.OneTimeOnly,
            RefreshTokenExpiration = TokenExpiration.Sliding,
            SlidingRefreshTokenLifetime = 8 * 60 * 60,
            AbsoluteRefreshTokenLifetime = 24 * 60 * 60,
            UpdateAccessTokenClaimsOnRefresh = true,
            AlwaysIncludeUserClaimsInIdToken = true,
            RequireConsent = false,
        };
        if (!string.IsNullOrEmpty(c.ClientSecret))
            client.ClientSecrets.Add(new Secret(c.ClientSecret.Sha256()));
        return client;
    });
}
