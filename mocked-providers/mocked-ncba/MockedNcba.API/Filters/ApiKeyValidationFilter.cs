using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MockedNcba.API.Filters;

/// <summary>
/// Action filter that validates NCBA API-Key and API-User request headers.
/// Returns 401 Unauthorized if either header is missing or empty.
/// </summary>
public class ApiKeyValidationFilter : IActionFilter
{
    private readonly ILogger<ApiKeyValidationFilter> _logger;

    public ApiKeyValidationFilter(ILogger<ApiKeyValidationFilter> logger)
    {
        _logger = logger;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var headers = context.HttpContext.Request.Headers;
        var apiKey  = headers["API-Key"].FirstOrDefault();
        var apiUser = headers["API-User"].FirstOrDefault();

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiUser))
        {
            _logger.LogWarning(
                "Rejected request missing NCBA auth headers: API-Key present={HasKey}, API-User present={HasUser}",
                !string.IsNullOrEmpty(apiKey), !string.IsNullOrEmpty(apiUser));

            context.Result = new UnauthorizedObjectResult(new
            {
                error = "Missing required authentication headers: API-Key and API-User"
            });
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
