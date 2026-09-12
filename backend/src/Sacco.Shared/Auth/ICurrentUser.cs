namespace Sacco.Shared.Auth;

/// <summary>The authenticated principal for the current request. Resolved from the validated token.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    string UserName { get; }
    /// <summary>Tenant slug claim from the token, if present.</summary>
    string? TenantSlug { get; }
}
