namespace Sacco.Shared.Audit;

/// <summary>
/// Append-only audit trail (ADR 0019). Every approval, denial, GL adjustment, KYC decision, sign-in and
/// configuration change is recorded here (SASRA governance trail). Implemented by the Platform module, which
/// enriches each event with request context (<see cref="IAuditContext"/>) and chains the rows so tampering shows.
/// </summary>
public interface IAuditLogger
{
    Task RecordAsync(AuditEvent auditEvent, CancellationToken ct);
}

/// <param name="Action">Dotted, past tense: "ledger.journal.approved".</param>
/// <param name="EntityType">The thing acted on: "JournalEntry".</param>
/// <param name="EntityId">Its id, or a natural key when there is no row (an account number, a report period).</param>
/// <param name="ActorUserId">Who did it. <see cref="Guid.Empty"/> for anonymous or system activity.</param>
/// <param name="Details">JSON object with what changed or why. Build it with <see cref="AuditDetails"/>.</param>
/// <param name="CorrelationId">Overrides the request's trace id; normally left null.</param>
/// <param name="Outcome">Whether the attempt succeeded — failures (refused sign-ins, blocked callbacks) matter as much as successes.</param>
public sealed record AuditEvent(
    string Action,
    string EntityType,
    string EntityId,
    Guid ActorUserId,
    string? Details = null,
    string? CorrelationId = null,
    AuditOutcome Outcome = AuditOutcome.Success);

public enum AuditOutcome { Success, Failure, Denied }

/// <summary>
/// Request context added to every audit row by the Platform module: who (name), from where (IP, user agent), under
/// which request (trace id) and at which branch. Implemented in the API host over the current HttpContext; background
/// work (seeding, schedulers) uses the null implementation and records no request details.
/// </summary>
public interface IAuditContext
{
    string? ActorName { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }
    Guid? BranchId { get; }
}

public sealed class NullAuditContext : IAuditContext
{
    public string? ActorName => null;
    public string? IpAddress => null;
    public string? UserAgent => null;
    public string? CorrelationId => null;
    public Guid? BranchId => null;
}

/// <summary>
/// Builds the JSON for <see cref="AuditEvent.Details"/> without hand-written string interpolation, so values are
/// escaped and "what changed" reads consistently across modules.
/// </summary>
public sealed class AuditDetails
{
    private readonly Dictionary<string, object?> _values = [];

    public static AuditDetails New() => new();

    public AuditDetails With(string name, object? value)
    {
        _values[name] = value;
        return this;
    }

    /// <summary>Records a field that changed, as <c>{"field": {"from": …, "to": …}}</c>. Unchanged values are skipped.</summary>
    public AuditDetails Changed<T>(string name, T? before, T? after)
    {
        if (!EqualityComparer<T>.Default.Equals(before, after))
            _values[name] = new Dictionary<string, object?> { ["from"] = before, ["to"] = after };
        return this;
    }

    /// <summary>Set difference between two lists, as <c>{"name": {"added": […], "removed": […]}}</c>.</summary>
    public AuditDetails Diff<T>(string name, IEnumerable<T> before, IEnumerable<T> after)
    {
        var from = before.ToHashSet();
        var to = after.ToHashSet();
        var added = to.Except(from).ToList();
        var removed = from.Except(to).ToList();
        if (added.Count > 0 || removed.Count > 0)
            _values[name] = new Dictionary<string, object?> { ["added"] = added, ["removed"] = removed };
        return this;
    }

    public bool IsEmpty => _values.Count == 0;

    public override string ToString() => System.Text.Json.JsonSerializer.Serialize(_values);

    /// <summary>Null when nothing was recorded, so an empty object is never stored.</summary>
    public string? ToJson() => IsEmpty ? null : ToString();
}
