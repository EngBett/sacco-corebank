namespace Sacco.Shared.Auth;

/// <summary>The authenticated principal for the current request. Resolved from the validated token.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    string UserName { get; }
    /// <summary>Tenant slug claim from the token, if present.</summary>
    string? TenantSlug { get; }
    /// <summary>Set when the principal is a member self-service login (claim <c>member_id</c>); null for staff.</summary>
    Guid? MemberId { get; }
}

public static class CurrentUserExtensions
{
    /// <summary>The member behind a self-service call. Staff tokens carry no member id and are refused — a staff user must use the staff endpoints.</summary>
    public static Guid RequireMemberId(this ICurrentUser user)
        => user.MemberId ?? throw new Sacco.Shared.Domain.ForbiddenException("This endpoint is for member self-service logins.");
}
