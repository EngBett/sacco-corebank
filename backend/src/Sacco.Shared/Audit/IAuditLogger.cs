namespace Sacco.Shared.Audit;

/// <summary>
/// Append-only audit trail. Every approval, denial, GL adjustment, KYC decision and
/// configuration change is recorded here (SASRA governance trail). Implemented by the
/// Platform module; written inside the caller's unit of work where possible.
/// </summary>
public interface IAuditLogger
{
    Task RecordAsync(AuditEvent auditEvent, CancellationToken ct);
}

public sealed record AuditEvent(
    string Action,          // e.g. "ledger.journal.approved"
    string EntityType,      // e.g. "JournalEntry"
    string EntityId,
    Guid ActorUserId,
    string? Details = null, // JSON or free text
    string? CorrelationId = null);
