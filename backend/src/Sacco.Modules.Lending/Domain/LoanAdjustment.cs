using Sacco.Shared.Domain;
using Sacco.Shared.Lending;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Lending.Domain;

public enum LoanAdjustmentKind { WriteOff = 1, Restructure = 2 }
public enum LoanAdjustmentStatus { PendingApproval = 1, Approved = 2, Rejected = 3 }

/// <summary>
/// A write-off or restructuring request on an active loan. Both move money or change contractual terms,
/// so both are maker-checker: requested by one user, approved by a different one (non-negotiable #6).
/// The approval is what posts to the GL or rebuilds the schedule.
/// </summary>
public class LoanAdjustment : TenantEntity
{
    private LoanAdjustment() { }

    public Guid LoanId { get; private set; }
    public string LoanNumber { get; private set; } = string.Empty;
    public LoanAdjustmentKind Kind { get; private set; }
    public LoanAdjustmentStatus Status { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid RequestedByUserId { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionNotes { get; private set; }
    // Restructure terms
    public int? NewTermMonths { get; private set; }
    public int? NewInterestRateBps { get; private set; }
    // Write-off outcome (filled at approval)
    public decimal? PrincipalWrittenOff { get; private set; }
    public decimal? InterestReversed { get; private set; }
    public Guid? JournalEntryId { get; private set; }

    public static LoanAdjustment RequestWriteOff(Guid tenantId, Loan loan, string reason, Guid by, DateTimeOffset now)
    {
        if (loan.Status != LoanStatus.Active) throw new DomainRuleException("loans.not_active", $"Loan {loan.LoanNumber} is {loan.Status}.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainRuleException("loans.reason_required", "A reason is required.");
        return new LoanAdjustment { Id = Ids.New(), TenantId = tenantId, LoanId = loan.Id, LoanNumber = loan.LoanNumber, Kind = LoanAdjustmentKind.WriteOff, Status = LoanAdjustmentStatus.PendingApproval, Reason = reason.Trim(), RequestedByUserId = by, RequestedAt = now };
    }

    public static LoanAdjustment RequestRestructure(Guid tenantId, Loan loan, LoanProduct product, int newTermMonths, int? newInterestRateBps, string reason, Guid by, DateTimeOffset now)
    {
        if (loan.Status != LoanStatus.Active) throw new DomainRuleException("loans.not_active", $"Loan {loan.LoanNumber} is {loan.Status}.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainRuleException("loans.reason_required", "A reason is required.");
        if (newTermMonths < 1 || newTermMonths > product.MaxTermMonths)
            throw new DomainRuleException("loans.restructure.term", $"The new term must be between 1 and {product.MaxTermMonths} months for {product.Code}.");
        if (newInterestRateBps is < 0 or > 10_000) throw new DomainRuleException("loans.restructure.rate", "The interest rate must be between 0 and 100%.");
        return new LoanAdjustment { Id = Ids.New(), TenantId = tenantId, LoanId = loan.Id, LoanNumber = loan.LoanNumber, Kind = LoanAdjustmentKind.Restructure, Status = LoanAdjustmentStatus.PendingApproval, Reason = reason.Trim(), RequestedByUserId = by, RequestedAt = now, NewTermMonths = newTermMonths, NewInterestRateBps = newInterestRateBps };
    }

    public void Approve(Guid by, string? notes, DateTimeOffset now)
    {
        if (Status != LoanAdjustmentStatus.PendingApproval) throw new DomainRuleException("loans.adjustment.not_pending", $"Request is {Status}.");
        MakerChecker.EnsureDistinct(RequestedByUserId, by, $"{Kind} of loan {LoanNumber}");
        Status = LoanAdjustmentStatus.Approved; DecidedByUserId = by; DecidedAt = now; DecisionNotes = notes;
    }

    public void Reject(Guid by, string reason, DateTimeOffset now)
    {
        if (Status != LoanAdjustmentStatus.PendingApproval) throw new DomainRuleException("loans.adjustment.not_pending", $"Request is {Status}.");
        MakerChecker.EnsureDistinct(RequestedByUserId, by, $"{Kind} of loan {LoanNumber}");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainRuleException("loans.reason_required", "A rejection reason is required.");
        Status = LoanAdjustmentStatus.Rejected; DecidedByUserId = by; DecidedAt = now; DecisionNotes = reason.Trim();
    }

    internal void RecordWriteOff(decimal principal, decimal interestReversed, Guid journalEntryId)
    {
        PrincipalWrittenOff = principal; InterestReversed = interestReversed; JournalEntryId = journalEntryId;
    }
}
