using System.Security.Cryptography;
using System.Text;

namespace Sacco.Shared.Domain;

/// <summary>Well-known actor ids for postings the platform itself originates (provider callbacks, scheduled jobs).</summary>
public static class SystemActors
{
    public static readonly Guid System = Ids.Deterministic("user:system");
}

public static class Ids
{
    /// <summary>Time-ordered GUID (v7) — good index locality in Postgres.</summary>
    public static Guid New() => Guid.CreateVersion7();

    /// <summary>
    /// Deterministic GUID derived from a stable string key. Used by seed data so re-running
    /// the seeder is idempotent and cross-module references (member ↔ ledger account) line up
    /// without either module knowing about the other's tables.
    /// </summary>
    public static Guid Deterministic(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        // Mark as RFC 4122 variant / version 8 (custom) so it never collides with v7 ids.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
