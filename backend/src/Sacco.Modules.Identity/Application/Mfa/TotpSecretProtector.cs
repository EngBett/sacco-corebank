using System.Security.Cryptography;
using System.Text;

namespace Sacco.Modules.Identity.Application.Mfa;

/// <summary>Configuration section <c>Identity:Mfa</c>.</summary>
public sealed class MfaSettings
{
    public const string SectionName = "Identity:Mfa";

    /// <summary>Key id → base64-encoded 32-byte AES key. Keep old keys listed after rotating so existing secrets still decrypt.</summary>
    public Dictionary<string, string> EncryptionKeys { get; set; } = [];

    /// <summary>Key used to encrypt new secrets.</summary>
    public string? ActiveKeyId { get; set; }

    /// <summary>Name shown in the authenticator app next to the account; defaults to the tenant's short name.</summary>
    public string? IssuerOverride { get; set; }

    /// <summary>The development key committed in appsettings.Development.json — refused in Production.</summary>
    public const string DevelopmentKeyId = "dev";
}

/// <summary>
/// Encrypts authenticator secrets at rest with AES-256-GCM using a key held outside the database (ADR 0016), so a
/// database copy alone can't generate anyone's codes. Stored format: <c>{keyId}:{base64(nonce|tag|ciphertext)}</c>.
/// </summary>
public sealed class TotpSecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly Dictionary<string, byte[]> _keys;
    private readonly string _activeKeyId;

    public TotpSecretProtector(MfaSettings settings)
    {
        _keys = settings.EncryptionKeys.ToDictionary(k => k.Key, k => Convert.FromBase64String(k.Value));
        if (_keys.Values.Any(k => k.Length != 32))
            throw new InvalidOperationException("Identity:Mfa:EncryptionKeys must be base64-encoded 32-byte keys.");
        _activeKeyId = settings.ActiveKeyId ?? throw new InvalidOperationException("Identity:Mfa:ActiveKeyId is not configured.");
        if (!_keys.ContainsKey(_activeKeyId))
            throw new InvalidOperationException($"Identity:Mfa:ActiveKeyId '{_activeKeyId}' has no matching key in Identity:Mfa:EncryptionKeys.");
    }

    public string Protect(byte[] secret)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[secret.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_keys[_activeKeyId], TagSize);
        aes.Encrypt(nonce, secret, cipher, tag, AssociatedData(_activeKeyId));
        return $"{_activeKeyId}:{Convert.ToBase64String([.. nonce, .. tag, .. cipher])}";
    }

    public byte[] Unprotect(string protectedSecret)
    {
        var separator = protectedSecret.IndexOf(':');
        if (separator <= 0) throw new CryptographicException("Malformed protected secret.");
        var keyId = protectedSecret[..separator];
        if (!_keys.TryGetValue(keyId, out var key)) throw new CryptographicException($"No MFA encryption key '{keyId}' is configured.");
        var payload = Convert.FromBase64String(protectedSecret[(separator + 1)..]);
        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain, AssociatedData(keyId));
        return plain;
    }

    private static byte[] AssociatedData(string keyId) => Encoding.UTF8.GetBytes($"sacco.identity.totp:{keyId}");
}
