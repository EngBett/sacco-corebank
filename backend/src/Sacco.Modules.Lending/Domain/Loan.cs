using Sacco.Shared.Domain;
using Sacco.Shared.Lending;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Lending.Domain;

public enum GuarantorStatus { Pending = 1, Accepted = 2, Declined = 3, Released = 4 }
public enum ApprovalDecision { Approve = 1, Reject = 2 }
public enum InstallmentStatus { Pending = 1, PartiallyPaid = 2, Paid = 3 }

/// <summary>Eligibility as evaluated at application time; kept for the audit trail.</summary>
public class EligibilitySnapshot
{
    public decimal BosaDeposits { get; set; }
    public decimal Shares { get; set; }
    public decimal DepositMultiplier { get; set; }
    public decimal MaxEligibleAmount { get; set; }
    public int MembershipMonths { get; set; }
    public int ContributionMonths { get; set; }
    public decimal ExistingOutstanding { get; set; }
}

/// <summary>
/// A member loan through its whole life. Approval is a first-class N-of-M workflow with
/// segregation of duties (no approver may be the originator or appraiser; the disburser may not
/// be the originator). Outstanding principal is the balance of the loan's ledger sub-account.
/// </summary>
public class Loan : TenantEntity
{
    private readonly List<LoanGuarantor> _guarantors = [];
    private readonly List<LoanApproval> _approvals = [];
    private readonly List<RepaymentInstallment> _schedule = [];
    private Loan() { }

    public string LoanNumber { get; private set; } = string.Empty;
    public Guid MemberId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductCode { get; private set; } = string.Empty;
    public Segment Segment { get; private set; }
    public decimal Amount { get; private set; }
    public int TermMonths { get; private set; }
    public int InterestRateBps { get; private set; }
    public InterestMethod InterestMethod { get; private set; }
    public string Purpose { get; private set; } = string.Empty;
    public LoanStatus Status { get; private set; }
    public EligibilitySnapshot Eligibility { get; private set; } = new();
    /// <summary>Member FOSA account the loan is paid into and, by default, repaid from.</summary>
    public string DisbursementAccountNumber { get; private set; } = string.Empty;
    /// <summary>BOSA deposits account pledged (held) as own security.</summary>
    public string? PledgedDepositsAccountNumber { get; private set; }
    public decimal PledgedDepositsAmount { get; private set; }
    public string? LedgerAccountNumber { get; private set; }
    public DateTimeOffset AppliedAt { get; private set; }
    public Guid AppliedByUserId { get; private set; }
    public Guid? AppraisedByUserId { get; private set; }
    public DateTimeOffset? AppraisedAt { get; private set; }
    public string? AppraisalNotes { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public DateTimeOffset? DisbursedAt { get; private set; }
    public DateOnly? DisbursementDate { get; private set; }
    public Guid? DisbursedByUserId { get; private set; }
    public decimal ProcessingFee { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    /// <summary>When the member consented to a credit bureau check for this application, and the wording they saw.</summary>
    public DateTimeOffset? BureauConsentAt { get; private set; }
    public string? BureauConsentText { get; private set; }
    public int RestructureCount { get; private set; }
    public IReadOnlyList<LoanGuarantor> Guarantors => _guarantors;
    public IReadOnlyList<LoanApproval> Approvals => _approvals;
    public IReadOnlyList<RepaymentInstallment> Schedule => _schedule;

    public static Loan Apply(Guid id, Guid tenantId, string loanNumber, Guid memberId, LoanProduct product, decimal amount, int termMonths, string purpose, string disbursementAccount,
        EligibilitySnapshot eligibility, Guid appliedBy, DateTimeOffset now)
    {
        if (!product.IsActive) throw new DomainRuleException("loans.product.inactive", $"{product.Code} is not active.");
        if (amount < product.MinAmount || amount > product.MaxAmount)
            throw new DomainRuleException("loans.amount_out_of_range", $"{product.Code} amounts must be between {product.MinAmount:N2} and {product.MaxAmount:N2}.");
        if (decimal.Round(amount, 2) != amount) throw new DomainRuleException("loans.amount_precision", "Amount must have at most two decimals.");
        if (termMonths < product.MinTermMonths || termMonths > product.MaxTermMonths)
            throw new DomainRuleException("loans.term_out_of_range", $"{product.Code} terms must be between {product.MinTermMonths} and {product.MaxTermMonths} months.");
        if (product.DepositMultiplier > 0 && amount > eligibility.MaxEligibleAmount)
            throw new DomainRuleException("loans.exceeds_eligibility", $"Maximum eligible amount is {eligibility.MaxEligibleAmount:N2} ({product.DepositMultiplier}× BOSA deposits of {eligibility.BosaDeposits:N2}).");
        if (eligibility.MembershipMonths < product.MinMembershipMonths)
            throw new DomainRuleException("loans.membership_too_short", $"{product.Code} requires {product.MinMembershipMonths} months of membership.");
        if (string.IsNullOrWhiteSpace(purpose)) throw new DomainRuleException("loans.purpose_required", "Loan purpose is required.");
        if (string.IsNullOrWhiteSpace(disbursementAccount)) throw new DomainRuleException("loans.disbursement_account_required", "A FOSA disbursement account is required.");

        return new Loan
        {
            Id = id, TenantId = tenantId, LoanNumber = loanNumber, MemberId = memberId, ProductId = product.Id, ProductCode = product.Code, Segment = product.Segment,
            Amount = amount, TermMonths = termMonths, InterestRateBps = product.InterestRateBps, InterestMethod = product.InterestMethod, Purpose = purpose.Trim(),
            Status = LoanStatus.Applied, Eligibility = eligibility, DisbursementAccountNumber = disbursementAccount, AppliedAt = now, AppliedByUserId = appliedBy,
            ProcessingFee = decimal.Round(amount * product.ProcessingFeeBps / 10_000m, 2, MidpointRounding.ToEven),
        };
    }

    // ---- Guarantors ----

    public LoanGuarantor AddGuarantor(Guid guarantorMemberId, string guarantorDepositsAccount, decimal amount, DateTimeOffset now)
    {
        if (Status is not (LoanStatus.Applied or LoanStatus.Appraised or LoanStatus.PendingApproval))
            throw new DomainRuleException("loans.guarantor.too_late", "Guarantors can only be added before approval.");
        if (guarantorMemberId == MemberId) throw new DomainRuleException("loans.guarantor.self", "A member cannot guarantee their own loan.");
        if (amount <= 0) throw new DomainRuleException("loans.guarantor.amount", "Guaranteed amount must be positive.");
        if (_guarantors.Any(g => g.GuarantorMemberId == guarantorMemberId && g.Status != GuarantorStatus.Declined))
            throw new ConflictException("loans.guarantor.duplicate", "This member is already a guarantor on the loan.");
        var g = LoanGuarantor.Create(Id, guarantorMemberId, guarantorDepositsAccount, amount, now);
        _guarantors.Add(g);
        return g;
    }

    public decimal AcceptedGuarantees => _guarantors.Where(g => g.Status == GuarantorStatus.Accepted).Sum(g => g.AmountGuaranteed);

    // ---- Appraisal & approval ----

    public void Appraise(Guid appraiser, string notes, DateTimeOffset now)
    {
        if (Status != LoanStatus.Applied) throw new DomainRuleException("loans.not_applied", $"Loan {LoanNumber} is {Status}.");
        if (string.IsNullOrWhiteSpace(notes)) throw new DomainRuleException("loans.appraisal_notes_required", "Appraisal notes are required.");
        AppraisedByUserId = appraiser; AppraisedAt = now; AppraisalNotes = notes.Trim();
        Status = LoanStatus.PendingApproval;
    }

    /// <summary>Records one approval. Returns true when the product's N-of-M requirement is now met and the loan is Approved.</summary>
    public bool Approve(Guid approver, string? notes, LoanProduct product, decimal borrowerDepositsAvailable, DateTimeOffset now)
    {
        if (Status != LoanStatus.PendingApproval) throw new DomainRuleException("loans.not_pending_approval", $"Loan {LoanNumber} is {Status}.");
        MakerChecker.EnsureDistinct(AppliedByUserId, approver, $"loan {LoanNumber} (originator)");
        if (AppraisedByUserId is Guid appraiser) MakerChecker.EnsureDistinct(appraiser, approver, $"loan {LoanNumber} (appraiser)");
        if (_approvals.Any(a => a.ApproverUserId == approver))
            throw new DomainRuleException("loans.already_approved_by_user", "This user has already recorded a decision on the loan.");

        if (product.RequiresGuarantors)
        {
            var accepted = _guarantors.Count(g => g.Status == GuarantorStatus.Accepted);
            if (accepted < product.MinGuarantors)
                throw new DomainRuleException("loans.guarantors_insufficient", $"{product.Code} requires {product.MinGuarantors} accepted guarantor(s); the loan has {accepted}.");
            var security = AcceptedGuarantees + Math.Min(borrowerDepositsAvailable, Amount);
            if (security < Amount)
                throw new DomainRuleException("loans.undersecured", $"Security {security:N2} (guarantees {AcceptedGuarantees:N2} + own deposits) is below the loan amount {Amount:N2}.");
        }

        _approvals.Add(LoanApproval.Create(Id, approver, ApprovalDecision.Approve, notes, now));
        var required = product.ApprovalsRequiredFor(Amount);
        if (_approvals.Count(a => a.Decision == ApprovalDecision.Approve) >= required)
        {
            Status = LoanStatus.Approved; ApprovedAt = now;
            return true;
        }
        return false;
    }

    public void Reject(Guid by, string reason, DateTimeOffset now)
    {
        if (Status is not (LoanStatus.Applied or LoanStatus.Appraised or LoanStatus.PendingApproval or LoanStatus.Approved))
            throw new DomainRuleException("loans.cannot_reject", $"Loan {LoanNumber} is {Status}.");
        MakerChecker.EnsureDistinct(AppliedByUserId, by, $"loan {LoanNumber} rejection");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainRuleException("loans.reason_required", "A rejection reason is required.");
        _approvals.Add(LoanApproval.Create(Id, by, ApprovalDecision.Reject, reason, now));
        Status = LoanStatus.Rejected; RejectionReason = reason.Trim();
        foreach (var g in _guarantors.Where(g => g.Status == GuarantorStatus.Accepted)) g.Release();
    }

    public void PledgeDeposits(string account, decimal amount) { PledgedDepositsAccountNumber = account; PledgedDepositsAmount = amount; }
    public void ClearPledge() { PledgedDepositsAccountNumber = null; PledgedDepositsAmount = 0; }

    public void RecordBureauConsent(DateTimeOffset now, string text) { BureauConsentAt = now; BureauConsentText = text; }

    /// <summary>Write-off: the balance sheet movement is the caller's; here the contract ends and guarantees are released.</summary>
    public void WriteOff(DateTimeOffset now)
    {
        if (Status != LoanStatus.Active) throw new DomainRuleException("loans.not_active", $"Loan {LoanNumber} is {Status}.");
        Status = LoanStatus.WrittenOff; ClosedAt = now;
        foreach (var g in _guarantors.Where(g => g.Status == GuarantorStatus.Accepted)) g.Release();
        ClearPledge();
    }

    /// <summary>Replaces the schedule with a new one over <paramref name="outstandingPrincipal"/>; interest already due but unpaid is carried into the first instalment. Returns the new rows for the caller to register as Added.</summary>
    public IReadOnlyList<RepaymentInstallment> Restructure(decimal outstandingPrincipal, int newTermMonths, int newRateBps, decimal carriedInterest, DateOnly start, DateTimeOffset now)
    {
        if (Status != LoanStatus.Active) throw new DomainRuleException("loans.not_active", $"Loan {LoanNumber} is {Status}.");
        if (outstandingPrincipal <= 0) throw new DomainRuleException("loans.restructure.nothing_outstanding", "There is no outstanding principal to restructure.");
        TermMonths = newTermMonths; InterestRateBps = newRateBps; RestructureCount++;
        _schedule.Clear();
        var fresh = BuildSchedule(Id, outstandingPrincipal, newRateBps, newTermMonths, InterestMethod, start);
        if (carriedInterest > 0) fresh[0].CarryInterest(carriedInterest);
        _schedule.AddRange(fresh);
        _ = now;
        return fresh;
    }

    /// <summary>Settles the loan in full: all outstanding principal plus interest due on or before <paramref name="asOf"/>; interest on later instalments is waived. Returns the (interest, principal, accruedInterestPaid) split.</summary>
    public (decimal Interest, decimal Principal, decimal AccruedInterestPaid) Payoff(DateOnly asOf, DateTimeOffset now)
    {
        if (Status != LoanStatus.Active) throw new DomainRuleException("loans.not_active", $"Loan {LoanNumber} is {Status}.");
        decimal interest = 0, principal = 0, accrued = 0;
        foreach (var inst in _schedule.Where(i => i.Status != InstallmentStatus.Paid).OrderBy(i => i.Number))
        {
            if (inst.DueDate > asOf) inst.WaiveInterest();
            var (i, p, a) = inst.Apply(inst.Outstanding, now);
            interest += i; principal += p; accrued += a;
        }
        return (interest, principal, accrued);
    }

    // ---- Disbursement & schedule ----

    public void Disburse(Guid disburser, string ledgerAccountNumber, DateOnly date, DateTimeOffset now)
    {
        if (Status != LoanStatus.Approved) throw new DomainRuleException("loans.not_approved", $"Loan {LoanNumber} is {Status}.");
        MakerChecker.EnsureDistinct(AppliedByUserId, disburser, $"loan {LoanNumber} disbursement");
        LedgerAccountNumber = ledgerAccountNumber; DisbursedByUserId = disburser; DisbursedAt = now; DisbursementDate = date;
        Status = LoanStatus.Active;
        _schedule.Clear();
        _schedule.AddRange(BuildSchedule(Id, Amount, InterestRateBps, TermMonths, InterestMethod, date));
    }

    public static IReadOnlyList<RepaymentInstallment> BuildSchedule(Guid loanId, decimal principal, int rateBps, int months, InterestMethod method, DateOnly start)
    {
        var list = new List<RepaymentInstallment>();
        var monthlyRate = rateBps / 10_000m / 12m;
        if (method == InterestMethod.Flat)
        {
            var totalInterest = decimal.Round(principal * rateBps / 10_000m * months / 12m, 2, MidpointRounding.ToEven);
            var interestEach = decimal.Round(totalInterest / months, 2, MidpointRounding.ToEven);
            var principalEach = decimal.Round(principal / months, 2, MidpointRounding.ToEven);
            decimal pAcc = 0, iAcc = 0;
            for (var n = 1; n <= months; n++)
            {
                var p = n == months ? principal - pAcc : principalEach;
                var i = n == months ? totalInterest - iAcc : interestEach;
                pAcc += p; iAcc += i;
                list.Add(RepaymentInstallment.Create(loanId, n, start.AddMonths(n), p, i));
            }
            return list;
        }

        // Reducing balance, equal instalments (annuity).
        decimal payment;
        if (monthlyRate == 0) payment = decimal.Round(principal / months, 2, MidpointRounding.ToEven);
        else
        {
            var factor = (double)monthlyRate / (1 - Math.Pow(1 + (double)monthlyRate, -months));
            payment = decimal.Round(principal * (decimal)factor, 2, MidpointRounding.ToEven);
        }
        var balance = principal;
        for (var n = 1; n <= months; n++)
        {
            var interest = decimal.Round(balance * monthlyRate, 2, MidpointRounding.ToEven);
            var principalPart = n == months ? balance : Math.Min(balance, payment - interest);
            balance -= principalPart;
            list.Add(RepaymentInstallment.Create(loanId, n, start.AddMonths(n), principalPart, interest));
        }
        return list;
    }

    /// <summary>Allocates a repayment across instalments in order: interest first, then principal. Returns the (interest, principal) split.</summary>
    public (decimal Interest, decimal Principal, decimal AccruedInterestPaid) AllocateRepayment(decimal amount, DateTimeOffset now)
    {
        if (Status != LoanStatus.Active) throw new DomainRuleException("loans.not_active", $"Loan {LoanNumber} is {Status}.");
        if (amount <= 0 || decimal.Round(amount, 2) != amount) throw new DomainRuleException("loans.repayment_amount", "Repayment must be a positive amount with at most two decimals.");
        decimal remaining = amount, interest = 0, principal = 0, accruedPaid = 0;
        foreach (var inst in _schedule.Where(i => i.Status != InstallmentStatus.Paid).OrderBy(i => i.Number))
        {
            if (remaining <= 0) break;
            var (i, p, accrued) = inst.Apply(remaining, now);
            interest += i; principal += p; accruedPaid += accrued; remaining -= i + p;
        }
        if (remaining > 0)
        {
            // Overpayment beyond the schedule: treat as extra principal against the last instalment.
            var last = _schedule.OrderBy(i => i.Number).Last();
            principal += remaining;
            last.ApplyExtraPrincipal(remaining);
            remaining = 0;
        }
        return (interest, principal, accruedPaid);
    }

    public bool IsFullyRepaid => _schedule.Count > 0 && _schedule.All(i => i.Status == InstallmentStatus.Paid);

    public void Close(DateTimeOffset now)
    {
        if (Status != LoanStatus.Active) throw new DomainRuleException("loans.not_active", $"Loan {LoanNumber} is {Status}.");
        Status = LoanStatus.Closed; ClosedAt = now;
        foreach (var g in _guarantors.Where(g => g.Status == GuarantorStatus.Accepted)) g.Release();
    }

    /// <summary>Days past the due date of the oldest unpaid instalment, less the grace period. 0 when current.</summary>
    public int DaysInArrears(DateOnly asOf, int graceDays)
    {
        var oldest = _schedule.Where(i => i.Status != InstallmentStatus.Paid && i.DueDate < asOf).OrderBy(i => i.DueDate).FirstOrDefault();
        if (oldest is null) return 0;
        var days = asOf.DayNumber - oldest.DueDate.DayNumber - graceDays;
        return Math.Max(0, days);
    }

    public decimal ArrearsAmount(DateOnly asOf) => _schedule.Where(i => i.DueDate < asOf).Sum(i => i.PrincipalDue - i.PrincipalPaid + i.InterestDue - i.InterestPaid);
}

public class LoanGuarantor
{
    private LoanGuarantor() { }
    public Guid Id { get; private set; }
    public Guid LoanId { get; private set; }
    public Guid GuarantorMemberId { get; private set; }
    /// <summary>The guarantor's BOSA deposits account on which the commitment is held.</summary>
    public string DepositsAccountNumber { get; private set; } = string.Empty;
    public decimal AmountGuaranteed { get; private set; }
    public GuarantorStatus Status { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }

    internal static LoanGuarantor Create(Guid loanId, Guid memberId, string account, decimal amount, DateTimeOffset now)
        => new() { Id = Ids.New(), LoanId = loanId, GuarantorMemberId = memberId, DepositsAccountNumber = account, AmountGuaranteed = amount, Status = GuarantorStatus.Pending, AddedAt = now };

    public void Accept(DateTimeOffset now)
    {
        if (Status != GuarantorStatus.Pending) throw new DomainRuleException("loans.guarantor.not_pending", "Guarantee is not pending.");
        Status = GuarantorStatus.Accepted; AcceptedAt = now;
    }
    public void Decline()
    {
        if (Status != GuarantorStatus.Pending) throw new DomainRuleException("loans.guarantor.not_pending", "Guarantee is not pending.");
        Status = GuarantorStatus.Declined;
    }
    internal void Release() { Status = GuarantorStatus.Released; ReleasedAt = DateTimeOffset.UtcNow; }
}

public class LoanApproval
{
    private LoanApproval() { }
    public Guid Id { get; private set; }
    public Guid LoanId { get; private set; }
    public Guid ApproverUserId { get; private set; }
    public ApprovalDecision Decision { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; }
    internal static LoanApproval Create(Guid loanId, Guid approver, ApprovalDecision decision, string? notes, DateTimeOffset now)
        => new() { Id = Ids.New(), LoanId = loanId, ApproverUserId = approver, Decision = decision, Notes = notes, DecidedAt = now };
}

public class RepaymentInstallment
{
    private RepaymentInstallment() { }
    public Guid Id { get; private set; }
    public Guid LoanId { get; private set; }
    public int Number { get; private set; }
    public DateOnly DueDate { get; private set; }
    public decimal PrincipalDue { get; private set; }
    public decimal InterestDue { get; private set; }
    public decimal PrincipalPaid { get; private set; }
    public decimal InterestPaid { get; private set; }
    /// <summary>Interest recognised in the GL (Dr receivable / Cr income) on or after the due date.</summary>
    public bool InterestAccrued { get; private set; }
    public InstallmentStatus Status { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }

    public decimal TotalDue => PrincipalDue + InterestDue;
    public decimal Outstanding => TotalDue - PrincipalPaid - InterestPaid;

    internal static RepaymentInstallment Create(Guid loanId, int n, DateOnly due, decimal principal, decimal interest)
        => new() { Id = Ids.New(), LoanId = loanId, Number = n, DueDate = due, PrincipalDue = principal, InterestDue = interest, Status = InstallmentStatus.Pending };

    internal void MarkAccrued() => InterestAccrued = true;

    /// <summary>Applies an amount: interest outstanding first, then principal. Returns (interestApplied, principalApplied, interestAppliedThatWasAlreadyAccrued).</summary>
    internal (decimal Interest, decimal Principal, decimal Accrued) Apply(decimal amount, DateTimeOffset now)
    {
        var interestOutstanding = InterestDue - InterestPaid;
        var i = Math.Min(amount, interestOutstanding);
        var p = Math.Min(amount - i, PrincipalDue - PrincipalPaid);
        InterestPaid += i; PrincipalPaid += p;
        Status = Outstanding <= 0 ? InstallmentStatus.Paid : (PrincipalPaid + InterestPaid > 0 ? InstallmentStatus.PartiallyPaid : InstallmentStatus.Pending);
        if (Status == InstallmentStatus.Paid) PaidAt ??= now;
        return (i, p, InterestAccrued ? i : 0m);
    }

    internal void ApplyExtraPrincipal(decimal amount) { PrincipalDue += amount; PrincipalPaid += amount; }
    internal void CarryInterest(decimal amount) => InterestDue += amount;
    /// <summary>Early settlement: interest not yet due is forgiven.</summary>
    internal void WaiveInterest() => InterestDue = InterestPaid;

    /// <summary>Days late this instalment was settled (0 when paid on time or still open).</summary>
    public int DaysLate(int graceDays) => PaidAt is null ? 0 : Math.Max(0, DateOnly.FromDateTime(PaidAt.Value.UtcDateTime).DayNumber - DueDate.DayNumber - graceDays);
}
