using System.Security.Cryptography;
using System.Text;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Identity.Domain;

public enum StaffTokenPurpose { Activation, PasswordReset }

/// <summary>
/// A single-use link token for account activation or password reset (ADR 0016). Only a SHA-256 hash is stored: the raw
/// token exists in the emailed link and nowhere else, so a database read can't be turned into a working link.
/// </summary>
public class StaffAccountToken : TenantEntity
{
    public static readonly TimeSpan ActivationLifetime = TimeSpan.FromHours(72);
    public static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromMinutes(60);

    private StaffAccountToken() { }

    public Guid UserId { get; private set; }
    public StaffTokenPurpose Purpose { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    public static (StaffAccountToken Token, string RawToken) Issue(Guid id, Guid tenantId, Guid userId, StaffTokenPurpose purpose, Guid createdBy, DateTimeOffset now)
    {
        var raw = Base64Url(RandomNumberGenerator.GetBytes(32));
        var token = new StaffAccountToken
        {
            Id = id,
            TenantId = tenantId,
            UserId = userId,
            Purpose = purpose,
            TokenHash = Hash(raw),
            CreatedAt = now,
            ExpiresAt = now + (purpose == StaffTokenPurpose.Activation ? ActivationLifetime : PasswordResetLifetime),
            CreatedByUserId = createdBy,
        };
        return (token, raw);
    }

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && ExpiresAt > now;

    /// <summary>Marks the token used; also how outstanding tokens are invalidated when a newer one is issued.</summary>
    public void Consume(DateTimeOffset now) => UsedAt ??= now;

    public static string Hash(string rawToken) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>One-time backup code for signing in without the authenticator app. Stored hashed; shown to the user once.</summary>
public class StaffRecoveryCode : TenantEntity
{
    public const int CodesPerUser = 10;

    private StaffRecoveryCode() { }

    public Guid UserId { get; private set; }
    public string CodeHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }

    /// <summary>Generates a fresh set. Codes look like <c>k7p2m-x9qd4</c> (50 bits each, unambiguous alphabet).</summary>
    public static (List<StaffRecoveryCode> Codes, List<string> RawCodes) GenerateSet(Guid tenantId, Guid userId, DateTimeOffset now, Func<Guid> newId)
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
        var codes = new List<StaffRecoveryCode>();
        var raw = new List<string>();
        for (var i = 0; i < CodesPerUser; i++)
        {
            var chars = Enumerable.Range(0, 10).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray();
            var code = $"{new string(chars, 0, 5)}-{new string(chars, 5, 5)}";
            raw.Add(code);
            codes.Add(new StaffRecoveryCode { Id = newId(), TenantId = tenantId, UserId = userId, CodeHash = Hash(code), CreatedAt = now });
        }
        return (codes, raw);
    }

    /// <summary>Case, spaces and the hyphen don't matter when typing a code back.</summary>
    public static string Normalize(string input) => new string(input.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray()) is { Length: 10 } s
        ? $"{s[..5]}-{s[5..]}"
        : input.Trim().ToLowerInvariant();

    public static string Hash(string code) => StaffAccountToken.Hash(Normalize(code));

    public void Use(DateTimeOffset now) => UsedAt ??= now;
}
