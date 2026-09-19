using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MockedMpesa.API.Configuration;
using MockedMpesa.API.Models;
using System.Text;

namespace MockedMpesa.API.Controllers;

[ApiController]
[Route("oauth/v1")]
public class OAuthController : ControllerBase
{
    private readonly ILogger<OAuthController> _logger;
    private readonly MpesaCredentialsOptions _credentials;

    public OAuthController(
        ILogger<OAuthController> logger,
        IOptions<MpesaCredentialsOptions> credentials)
    {
        _logger = logger;
        _credentials = credentials.Value;
    }

    [HttpGet("generate")]
    public IActionResult GenerateToken([FromQuery] string grant_type)
    {
        // Validate grant_type parameter
        if (string.IsNullOrEmpty(grant_type) || grant_type != "client_credentials")
        {
            _logger.LogError("Invalid grant_type");
            return BadRequest(new { error = "invalid_grant", error_description = "Unsupported grant type" });
        }
        
        _logger.LogInformation("Received token generation request with headers: {Headers}", Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()));

        // Check for Basic Auth header
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            _logger.LogWarning("Missing Authorization header");
            return Unauthorized(new { error = "invalid_client", error_description = "Invalid client credentials" });
        }

        var authHeader = Request.Headers["Authorization"].ToString();
        if (!authHeader.StartsWith("Basic "))
        {
            return Unauthorized(new { error = "invalid_client", error_description = "Invalid client credentials" });
        }

        // Decode and validate credentials
        try
        {
            var encodedCredentials = authHeader.Substring("Basic ".Length).Trim();
            var decodedBytes = Convert.FromBase64String(encodedCredentials);
            var credentials = Encoding.UTF8.GetString(decodedBytes);
            
            // Parse consumer_key:consumer_secret
            var parts = credentials.Split(':', 2);
            if (parts.Length != 2)
            {
                _logger.LogWarning("Invalid credentials format");
                return Unauthorized(new { error = "invalid_client", error_description = "Invalid client credentials" });
            }

            var consumerKey = parts[0];
            var consumerSecret = parts[1];

            // Validate against configured credentials
            if (consumerKey != _credentials.ConsumerKey || consumerSecret != _credentials.ConsumerSecret)
            {
                _logger.LogWarning("Invalid credentials provided. Expected ConsumerKey: {ExpectedKey}", _credentials.ConsumerKey);
                return Unauthorized(new { error = "invalid_client", error_description = "Invalid client credentials" });
            }
            
            _logger.LogInformation("OAuth token generated for valid credentials");
            
            return Ok(new TokenResponse
            {
                access_token = _credentials.BearerToken ?? string.Empty,
                expires_in = "3599"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate token");
            return Unauthorized(new { error = "Invalid credentials" });
        }
    }
}
