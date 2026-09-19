using System.Security.Cryptography;
using System.Text;

namespace Sacco.Modules.Identity.Application.Mfa;

/// <summary>
/// RFC 6238 time-based one-time passwords as Google Authenticator and Microsoft Authenticator expect them: HMAC-SHA1,
/// 6 digits, 30-second steps, a 160-bit secret shared as Base32. Small enough to own rather than take a dependency.
/// </summary>
public static class Totp
{
    public const int Digits = 6;
    public const int StepSeconds = 30;

    /// <summary>Codes one step either side of "now" are accepted, to absorb clock drift on the phone.</summary>
    public const int AllowedDriftSteps = 1;

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    public static string Compute(byte[] secret, long step, int digits = Digits)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--) { counter[i] = (byte)(step & 0xff); step >>= 8; }
        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        var modulo = (int)Math.Pow(10, digits);
        return (binary % modulo).ToString(new string('0', digits));
    }

    /// <summary>Returns the matching time step, or null. Compares in constant time.</summary>
    public static long? Verify(byte[] secret, string code, DateTimeOffset now)
    {
        code = new string(code.Where(char.IsDigit).ToArray());
        if (code.Length != Digits) return null;
        var current = StepAt(now);
        for (var step = current - AllowedDriftSteps; step <= current + AllowedDriftSteps; step++)
        {
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(secret, step)), Encoding.ASCII.GetBytes(code)))
                return step;
        }
        return null;
    }

    /// <summary>The <c>otpauth://</c> URI the authenticator app reads from the QR code.</summary>
    public static string ProvisioningUri(string issuer, string accountName, byte[] secret)
    {
        var label = Uri.EscapeDataString($"{issuer}:{accountName}");
        return $"otpauth://totp/{label}?secret={Base32Encode(secret)}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] Base32Decode(string text)
    {
        var clean = text.Replace(" ", "").TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var value = Base32Alphabet.IndexOf(c);
            if (value < 0) throw new FormatException($"'{c}' is not a Base32 character.");
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }
        return [.. output];
    }

    /// <summary>"JBSW Y3DP EHPK 3PXP" — groups of four are easier to type into an authenticator app by hand.</summary>
    public static string FormatForManualEntry(byte[] secret) =>
        string.Join(' ', Base32Encode(secret).Chunk(4).Select(chunk => new string(chunk)));
}
