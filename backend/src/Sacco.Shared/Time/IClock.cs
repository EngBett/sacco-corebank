namespace Sacco.Shared.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    DateOnly Today { get; }
}

public sealed class SystemClock : IClock
{
    // Kenya has no DST; business dates are computed in EAT (UTC+3).
    public static readonly TimeSpan KenyaOffset = TimeSpan.FromHours(3);
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(KenyaOffset).DateTime);
}
