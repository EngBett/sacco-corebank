using System.Security.Claims;
using Sacco.Shared.Auth;

namespace Sacco.Api.Infrastructure;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid UserId
    {
        get
        {
            var sub = Principal?.FindFirstValue("sub") ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
        }
    }

    public string UserName => Principal?.FindFirstValue("name") ?? Principal?.FindFirstValue("preferred_username") ?? Principal?.Identity?.Name ?? string.Empty;

    public string? TenantSlug => Principal?.FindFirstValue("tenant");

    public Guid? MemberId => Guid.TryParse(Principal?.FindFirstValue("member_id"), out var id) ? id : null;

    public Guid? BranchId => Guid.TryParse(Principal?.FindFirstValue("branch_id"), out var id) ? id : null;
}
