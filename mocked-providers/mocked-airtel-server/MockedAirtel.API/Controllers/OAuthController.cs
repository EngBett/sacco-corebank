using Microsoft.AspNetCore.Mvc;
using MockedAirtel.API.Models;
using System.Text;

namespace MockedAirtel.API.Controllers;

[ApiController]
[Route("auth/oauth2")]
public class OAuthController : ControllerBase
{
    private readonly ILogger<OAuthController> _logger;

    public OAuthController(ILogger<OAuthController> logger)
    {
        _logger = logger;
    }

    [HttpPost("token")]
    public IActionResult GenerateToken([FromHeader(Name = "Authorization")] string authorization)
    {
        // Check for Basic Auth header
        if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith("Basic "))
        {
            return Unauthorized(new { error = "invalid_client", error_description = "Invalid authorization header" });
        }

        // Decode and validate credentials (for testing, accept any valid base64)
        try
        {
            var encodedCredentials = authorization.Substring("Basic ".Length).Trim();
            var decodedBytes = Convert.FromBase64String(encodedCredentials);
            var credentials = Encoding.UTF8.GetString(decodedBytes);
            
            _logger.LogInformation("OAuth token generated for credentials: {Credentials}", credentials);

            // Generate a mock token
            var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=", "").Replace("+", "-").Replace("/", "_");
            
            return Ok(new TokenResponse
            {
                access_token = token,
                token_type = "Bearer",
                expires_in = 3600
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate token");
            return Unauthorized(new { error = "invalid_client", error_description = "Invalid credentials" });
        }
    }
}
