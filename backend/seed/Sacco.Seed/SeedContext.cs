using Microsoft.Extensions.Logging;
using Sacco.Shared.Time;

namespace Sacco.Seed;

/// <summary>Deterministic clock and shared state for the seeders. "Today" is fixed so seeded histories are stable across runs.</summary>
public sealed class SeedClock : IClock
{
    // Seed data is dated relative to this anchor so aging buckets etc. are reproducible in tests.
    public static readonly DateOnly Anchor = new(2026, 9, 11);
    public DateTimeOffset UtcNow => new(Anchor.ToDateTime(new TimeOnly(6, 0)), TimeSpan.Zero);
    public DateOnly Today => Anchor;
}

public interface ISeeder
{
    /// <summary>Lower runs first.</summary>
    int Order { get; }
    Task SeedAsync(CancellationToken ct);
}

public static class SeedLog
{
    public static void Created(this ILogger logger, string what, int count) => logger.LogInformation("  {What}: {Count} created", what, count);
}
