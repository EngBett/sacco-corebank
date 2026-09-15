using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MockedEquity.API.Models;
using MockedEquity.API.Services;

namespace MockedEquity.API.Controllers;

/// <summary>
/// Jenga merchant authentication. Lives outside /momo-apis on the real platform, and takes an
/// Api-Key header rather than a bearer token — reproduced here so clients need the same two-client
/// setup they need in production.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("authentication/api/v3")]
public class AuthenticationController : ControllerBase
{
    private readonly ITokenStore _tokens;
    private readonly JengaOptions _options;
    private readonly ILogger<AuthenticationController> _logger;

    public AuthenticationController(
        ITokenStore tokens,
        IOptions<JengaOptions> options,
        ILogger<AuthenticationController> logger)
    {
        _tokens = tokens;
        _options = options.Value;
        _logger = logger;
    }

    [HttpPost("authenticate/merchant")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Authenticate(
        [FromBody] AuthRequest request,
        [FromHeader(Name = "Api-Key")] string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey != _options.ApiKey)
        {
            _logger.LogWarning("Authentication rejected: Api-Key header missing or incorrect");
            return Unauthorized(new
            {
                message = "Unauthorized",
                code = "101",
                metadata = new { error = "Invalid Api-Key" }
            });
        }

        if (request.MerchantCode != _options.MerchantCode || request.ConsumerSecret != _options.ConsumerSecret)
        {
            _logger.LogWarning(
                "Authentication rejected for merchantCode {MerchantCode}: credentials do not match",
                request.MerchantCode);

            return Unauthorized(new
            {
                message = "Unauthorized",
                code = "101",
                metadata = new { error = "Invalid merchantCode or consumerSecret" }
            });
        }

        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_options.TokenLifetimeMinutes);
        var token = _tokens.Issue(expiresAt);

        _logger.LogInformation(
            "Issued token for merchant {MerchantCode}, valid until {ExpiresAt:O}",
            request.MerchantCode, expiresAt);

        return Ok(new AuthResponse
        {
            AccessToken = token,
            RefreshToken = Guid.NewGuid().ToString("N"),
            // Absolute timestamp, not a duration — the detail clients most often mishandle.
            ExpiresIn = expiresAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            IssuedAt = issuedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            TokenType = "Bearer"
        });
    }
}
