using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Reporting.Domain;

public enum ReturnStatus { Generated = 1, Submitted = 2, Withdrawn = 3 }

/// <summary>
/// A generated statutory return package for a period: an immutable JSON snapshot of every section
/// plus the reconciliation result. Submission is maker-checker (generator ≠ submitter).
/// </summary>
public class StatutoryReturn : TenantEntity
{
    private StatutoryReturn() { }

    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public ReturnStatus Status { get; private set; }
    public Guid GeneratedByUserId { get; private set; }
    public DateTimeOffset GeneratedAt { get; private set; }
    public Guid? SubmittedByUserId { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public string? SubmissionReference { get; private set; }
    public bool IsReconciled { get; private set; }
    public string ReconciliationNotes { get; private set; } = string.Empty;
    /// <summary>The full package as JSON (jsonb).</summary>
    public string Package { get; private set; } = "{}";
    public string? WithdrawalReason { get; private set; }

    public static StatutoryReturn Create(Guid id, Guid tenantId, DateOnly periodStart, DateOnly periodEnd, Guid generatedBy, DateTimeOffset now, bool reconciled, string notes, string packageJson)
    {
        if (periodEnd < periodStart) throw new DomainRuleException("reporting.period_invalid", "Period end must be on or after period start.");
        return new StatutoryReturn { Id = id, TenantId = tenantId, PeriodStart = periodStart, PeriodEnd = periodEnd, Status = ReturnStatus.Generated, GeneratedByUserId = generatedBy, GeneratedAt = now, IsReconciled = reconciled, ReconciliationNotes = notes, Package = packageJson };
    }

    public void Submit(Guid submitter, string submissionReference, DateTimeOffset now)
    {
        if (Status != ReturnStatus.Generated) throw new DomainRuleException("reporting.not_generated", $"Return is {Status}.");
        if (!IsReconciled) throw new DomainRuleException("reporting.not_reconciled", "A return that does not reconcile to the ledger cannot be submitted: " + ReconciliationNotes);
        MakerChecker.EnsureDistinct(GeneratedByUserId, submitter, $"statutory return {PeriodStart:yyyy-MM-dd}..{PeriodEnd:yyyy-MM-dd}");
        if (string.IsNullOrWhiteSpace(submissionReference)) throw new DomainRuleException("reporting.submission_reference_required", "Record the SASRA portal acknowledgement/reference.");
        Status = ReturnStatus.Submitted; SubmittedByUserId = submitter; SubmittedAt = now; SubmissionReference = submissionReference.Trim();
    }

    public void Withdraw(Guid by, string reason)
    {
        if (Status != ReturnStatus.Generated) throw new DomainRuleException("reporting.not_generated", $"Return is {Status}; submitted returns are immutable.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainRuleException("reporting.reason_required", "A reason is required.");
        Status = ReturnStatus.Withdrawn; WithdrawalReason = reason.Trim();
    }
}
