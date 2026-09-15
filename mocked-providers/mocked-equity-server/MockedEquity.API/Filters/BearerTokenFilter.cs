using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MockedEquity.API.Services;

namespace MockedEquity.API.Filters;

/// <summary>
/// Rejects payment requests without a live bearer token, so clients are forced to exercise the
/// authenticate → call → re-authenticate-on-401 path rather than skipping straight to payments.
/// </summary>
/// <remarks>
/// Marked endpoints opt out with <see cref="AllowAnonymousAttribute"/> — authentication itself, the
/// health check, and the test-control endpoints.
/// </remarks>
public class BearerTokenFilter : IActionFilter
{
    private readonly ITokenStore _tokens;
    private readonly ILogger<BearerTokenFilter> _logger;

    public BearerTokenFilter(ITokenStore tokens, ILogger<BearerTokenFilter> logger)
    {
        _tokens = tokens;
        _logger = logger;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var endpointAllowsAnonymous = context.ActionDescriptor.EndpointMetadata
            .OfType<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>()
            .Any();

        if (endpointAllowsAnonymous)
            return;

        var header = context.HttpContext.Request.Headers.Authorization.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(header)
            || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Rejected {Path}: no bearer token", context.HttpContext.Request.Path);
            context.Result = Unauthorized("Missing or malformed Authorization header");
            return;
        }

        var token = header["Bearer ".Length..].Trim();

        if (!_tokens.IsValid(token))
        {
            _logger.LogWarning(
                "Rejected {Path}: token is unknown or expired", context.HttpContext.Request.Path);
            context.Result = Unauthorized("Invalid or expired JWT token");
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    private static ObjectResult Unauthorized(string error) =>
        new(new
        {
            message = "Unauthorized",
            code = "101",
            metadata = new { error }
        })
        {
            StatusCode = StatusCodes.Status401Unauthorized
        };
}
