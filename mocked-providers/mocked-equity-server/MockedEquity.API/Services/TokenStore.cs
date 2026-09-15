using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace MockedEquity.API.Services;

/// <summary>Tracks the tokens the mock has issued, so expiry and 401-refresh can be exercised.</summary>
public interface ITokenStore
{
    /// <summary>Issues a token valid until <paramref name="expiresAt"/>.</summary>
    string Issue(DateTimeOffset expiresAt);

    bool IsValid(string token);

    /// <summary>Expires every issued token — used by the test-control endpoint to force a 401.</summary>
    int ExpireAll();
}

public class TokenStore : ITokenStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _tokens = new(StringComparer.Ordinal);

    public string Issue(DateTimeOffset expiresAt)
    {
        // Shaped like a JWT so client-side logging and parsing behave as they would in production,
        // but deliberately not signed — this is a mock, and nothing should be verifying it.
        var token = $"eyJhbGciOiJSUzUxMiJ9.{Base64Url(RandomNumberGenerator.GetBytes(24))}.{Base64Url(RandomNumberGenerator.GetBytes(32))}";
        _tokens[token] = expiresAt;
        return token;
    }

    public bool IsValid(string token) =>
        _tokens.TryGetValue(token, out var expiresAt) && expiresAt > DateTimeOffset.UtcNow;

    public int ExpireAll()
    {
        var expired = 0;
        foreach (var token in _tokens.Keys)
        {
            _tokens[token] = DateTimeOffset.UtcNow.AddSeconds(-1);
            expired++;
        }

        return expired;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
