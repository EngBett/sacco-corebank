using System.Security.Cryptography;
using System.Text;

namespace MockedEquity.API.Security;

/// <summary>
/// The Jenga PIN/password envelope: AES-256-GCM with a PBKDF2-HMAC-SHA256 key derived from the API
/// key, Base64 of <c>IV(12) || SALT(16) || CIPHERTEXT || TAG(16)</c>.
/// </summary>
/// <remarks>
/// This is an independent implementation of the same spec the payments service encrypts with. That
/// is the point: if the two ever disagree, the mock rejects the PIN and the test fails here rather
/// than in UAT against the real Equity gateway.
/// </remarks>
public static class JengaCredentialCipher
{
    private const int SaltLengthBytes = 16;
    private const int IvLengthBytes = 12;
    private const int TagLengthBytes = 16;
    private const int KeyLengthBytes = 32;
    private const int Pbkdf2Iterations = 65_536;

    /// <summary>
    /// Attempts to recover the plaintext credential. Returns false rather than throwing, so callers
    /// can answer with Jenga's own error code instead of a 500.
    /// </summary>
    public static bool TryDecrypt(string? base64Payload, string apiKey, out string? plainText)
    {
        plainText = null;

        if (string.IsNullOrWhiteSpace(base64Payload) || string.IsNullOrWhiteSpace(apiKey))
            return false;

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(base64Payload);
        }
        catch (FormatException)
        {
            return false;
        }

        if (payload.Length <= IvLengthBytes + SaltLengthBytes + TagLengthBytes)
            return false;

        var iv = payload.AsSpan(0, IvLengthBytes).ToArray();
        var salt = payload.AsSpan(IvLengthBytes, SaltLengthBytes).ToArray();
        var cipherLength = payload.Length - IvLengthBytes - SaltLengthBytes - TagLengthBytes;
        var cipherBytes = payload.AsSpan(IvLengthBytes + SaltLengthBytes, cipherLength).ToArray();
        var tag = payload.AsSpan(payload.Length - TagLengthBytes, TagLengthBytes).ToArray();

        var key = Rfc2898DeriveBytes.Pbkdf2(
            password: Encoding.UTF8.GetBytes(apiKey),
            salt: salt,
            iterations: Pbkdf2Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: KeyLengthBytes);

        var plainBytes = new byte[cipherLength];

        try
        {
            using var aes = new AesGcm(key, TagLengthBytes);
            aes.Decrypt(iv, cipherBytes, tag, plainBytes);
            plainText = Encoding.UTF8.GetString(plainBytes);
            return true;
        }
        catch (CryptographicException)
        {
            // Wrong key, tampered payload, or a layout that does not match the spec.
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    /// <summary>Encrypts a credential — used by the mock's own test tooling, not by the endpoints.</summary>
    public static string Encrypt(string plainText, string apiKey)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);
        var iv = RandomNumberGenerator.GetBytes(IvLengthBytes);

        var key = Rfc2898DeriveBytes.Pbkdf2(
            password: Encoding.UTF8.GetBytes(apiKey),
            salt: salt,
            iterations: Pbkdf2Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: KeyLengthBytes);

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagLengthBytes];

        try
        {
            using var aes = new AesGcm(key, TagLengthBytes);
            aes.Encrypt(iv, plainBytes, cipherBytes, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        return Convert.ToBase64String([.. iv, .. salt, .. cipherBytes, .. tag]);
    }
}
