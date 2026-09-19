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

    /// <summary>The staff member's branch (claim <c>branch_id</c>), stamped on what they do. Null for members and for unassigned staff.</summary>
    Guid? BranchId { get; }
}

/// <summary>
/// Stands in for the signed-in user when there is no request: the seed tool, schedulers and saga handlers. Nothing is
/// authenticated, so services that stamp "who did this" fall back to their own defaults (head-office branch, system actor).
/// </summary>
public sealed class NullCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;
    public Guid UserId => Guid.Empty;
    public string UserName => "system";
    public string? TenantSlug => null;
    public Guid? MemberId => null;
    public Guid? BranchId => null;
}

public static class CurrentUserExtensions
{
    /// <summary>The member behind a self-service call. Staff tokens carry no member id and are refused — a staff user must use the staff endpoints.</summary>
    public static Guid RequireMemberId(this ICurrentUser user)
        => user.MemberId ?? throw new Sacco.Shared.Domain.ForbiddenException("This endpoint is for member self-service logins.");
}
