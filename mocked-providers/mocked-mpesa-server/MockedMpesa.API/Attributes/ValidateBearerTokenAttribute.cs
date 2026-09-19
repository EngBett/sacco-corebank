using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using MockedMpesa.API.Configuration;

namespace MockedMpesa.API.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ValidateBearerTokenAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Get credentials from DI
        var credentials = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<MpesaCredentialsOptions>>()
            .Value;
        
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILogger<ValidateBearerTokenAttribute>>();

        // Check for Authorization header
        if (!context.HttpContext.Request.Headers.ContainsKey("Authorization"))
        {
            logger.LogWarning($"Missing Authorization Header: {context.HttpContext.Request.Headers["Authorization"]}");
            context.Result = new UnauthorizedObjectResult(new 
            { 
                errorMessage = "Missing Authorization header" 
            });
            return;
        }

        var authHeader = context.HttpContext.Request.Headers["Authorization"].ToString();
        
        // Validate Bearer token format
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new UnauthorizedObjectResult(new 
            { 
                errorMessage = "Invalid Authorization header format. Expected 'Bearer <token>'" 
            });
            return;
        }

        var token = authHeader.Substring("Bearer ".Length).Trim();
        
        // Validate token against configured BearerToken
        if (string.IsNullOrEmpty(credentials.BearerToken) || token != credentials.BearerToken)
        {
            context.Result = new UnauthorizedObjectResult(new 
            { 
                errorMessage = "Invalid Bearer token" 
            });
            return;
        }
    }
}
