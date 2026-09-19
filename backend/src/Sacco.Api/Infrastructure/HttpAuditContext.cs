using System.Diagnostics;
using Sacco.Shared.Audit;
using Sacco.Shared.Auth;

namespace Sacco.Api.Infrastructure;

/// <summary>
/// Request details every audit entry is stamped with (ADR 0019): who was signed in, from which address and browser,
/// under which request, and at which branch. Outside a request (seeding, schedulers) everything is null.
/// </summary>
public sealed class HttpAuditContext(IHttpContextAccessor accessor, ICurrentUser user) : IAuditContext
{
    public string? ActorName => string.IsNullOrWhiteSpace(user.UserName) ? null : user.UserName;

    /// <summary>The client address as the proxy reported it (ForwardedHeaders is configured in the host), else the socket address.</summary>
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

    /// <summary>The W3C trace id, so an audit entry ties back to the request's logs.</summary>
    public string? CorrelationId => Activity.Current?.TraceId.ToString() ?? accessor.HttpContext?.TraceIdentifier;

    public Guid? BranchId => user.BranchId;
}
