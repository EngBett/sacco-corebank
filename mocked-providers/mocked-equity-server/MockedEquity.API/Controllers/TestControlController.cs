using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MockedEquity.API.Models;
using MockedEquity.API.Security;
using MockedEquity.API.Services;

namespace MockedEquity.API.Controllers;

/// <summary>
/// Endpoints that exist only in the mock, for driving tests. None of these are on the real Jenga
/// platform — they are namespaced under /_test so they can never be mistaken for one.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("_test")]
public class TestControlController : ControllerBase
{
    private readonly ITransactionStore _store;
    private readonly ITokenStore _tokens;
    private readonly JengaOptions _options;
    private readonly ILogger<TestControlController> _logger;

    public TestControlController(
        ITransactionStore store,
        ITokenStore tokens,
        IOptions<JengaOptions> options,
        ILogger<TestControlController> logger)
    {
        _store = store;
        _tokens = tokens;
        _options = options.Value;
        _logger = logger;
    }

    [HttpGet("health")]
    public IActionResult Health() =>
        Ok(new
        {
            status = "healthy",
            service = "Mock Equity (Finserve Jenga DFS)",
            transactions = _store.All().Count,
            timestamp = DateTime.UtcNow
        });

    /// <summary>Every transaction the mock has seen, newest first.</summary>
    [HttpGet("transactions")]
    public IActionResult Transactions() =>
        Ok(_store.All().OrderByDescending(t => t.CreatedAt));

    /// <summary>One transaction, by request id or transaction id.</summary>
    [HttpGet("transactions/{identifier}")]
    public IActionResult Transaction(string identifier) =>
        _store.Find(identifier) is { } transaction ? Ok(transaction) : NotFound();

    /// <summary>
    /// Expires every issued token, so the next payment call gets a 401 and the client's
    /// refresh-and-retry path runs.
    /// </summary>
    [HttpPost("expire-tokens")]
    public IActionResult ExpireTokens()
    {
        var count = _tokens.ExpireAll();
        _logger.LogInformation("Expired {Count} token(s) on request from a test", count);
        return Ok(new { expired = count });
    }

    /// <summary>
    /// Encrypts a value the way a client must, so a shell script or another language can build a
    /// valid request without reimplementing the envelope.
    /// </summary>
    [HttpPost("encrypt")]
    public IActionResult Encrypt([FromBody] EncryptRequest request)
    {
        var plain = request.Value ?? _options.Pin;
        return Ok(new { encrypted = JengaCredentialCipher.Encrypt(plain, _options.ApiKey) });
    }

    /// <summary>
    /// Verifies a client-produced payload decrypts to what the mock expects — the fastest way to
    /// diagnose "why does Equity keep saying invalid credentials".
    /// </summary>
    [HttpPost("decrypt")]
    public IActionResult Decrypt([FromBody] DecryptRequest request)
    {
        if (!JengaCredentialCipher.TryDecrypt(request.Encrypted, _options.ApiKey, out var plain))
        {
            return BadRequest(new
            {
                valid = false,
                reason = "Payload did not decrypt. Expected Base64 of IV(12) || SALT(16) || CIPHERTEXT || TAG(16), "
                         + "PBKDF2-HMAC-SHA256(apiKey, salt, 65536) → 256-bit AES-GCM key."
            });
        }

        return Ok(new { valid = true, plainText = plain, matchesConfiguredPin = plain == _options.Pin });
    }

    /// <summary>Forgets every transaction, so a test run starts from a clean slate.</summary>
    [HttpPost("reset")]
    public IActionResult Reset()
    {
        var cleared = _store.All().Count;
        _store.Clear();

        _logger.LogInformation("Cleared {Count} transaction(s) on request from a test", cleared);
        return Ok(new { cleared });
    }

    public record EncryptRequest(string? Value);

    public record DecryptRequest(string? Encrypted);
}
