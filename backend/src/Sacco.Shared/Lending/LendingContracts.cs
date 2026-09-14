namespace Sacco.Shared.Lending;

public enum LoanStatus
{
    Applied = 1,
    Appraised = 2,
    PendingApproval = 3,
    Approved = 4,
    Active = 5,
    Closed = 6,
    Rejected = 7,
    Cancelled = 8,
    WrittenOff = 9,
}

public enum RepaymentChannel
{
    /// <summary>Debit the member's FOSA account.</summary>
    FosaAccount = 1,
    Cash = 2,
    MPesa = 3,
    AirtelMoney = 4,
    BankTransfer = 5,
    CheckOff = 6,
    /// <summary>Offset against the member's own BOSA deposits (exit settlement, or a member's instruction).</summary>
    DepositsOffset = 7,
}

public sealed record RepaymentCommand(
    string LoanNumber,
    decimal Amount,
    RepaymentChannel Channel,
    /// <summary>Idempotency key: receipt number / provider transaction reference.</summary>
    string Reference,
    string? Narrative,
    Guid ByUserId,
    /// <summary>Value date of the payment (defaults to today). Instalments record it as their paid date.</summary>
    DateOnly? PaidOn = null);

public sealed record RepaymentResult(Guid JournalEntryId, string LoanNumber, decimal PrincipalPaid, decimal InterestPaid, decimal OutstandingPrincipal, bool LoanClosed);

public sealed record MemberLoanExposure(Guid MemberId, decimal OutstandingPrincipal, decimal ArrearsAmount, int ActiveLoans, int LoansInArrears, decimal ActiveGuaranteesAmount, int ActiveGuarantees);

public sealed record AgingBucketSnapshot(string Name, int MinDaysInArrears, int ProvisionRateBps, int Loans, decimal Outstanding, decimal ProvisionRequired);
public sealed record MemberOutstandingSnapshot(Guid MemberId, int Loans, decimal Outstanding);
public sealed record PortfolioQualitySnapshot(DateOnly AsOf, decimal TotalOutstanding, decimal NonPerformingOutstanding, decimal ProvisionRequired, IReadOnlyList<AgingBucketSnapshot> Buckets, IReadOnlyList<MemberOutstandingSnapshot> ByMember, string? ConfigSource);

/// <summary>Lending module's public surface for Savings (exit settlement), Payments (repayments) and Reporting.</summary>
public sealed record MemberLoanSnapshot(Guid Id, string LoanNumber, string ProductCode, decimal Amount, LoanStatus Status, decimal OutstandingPrincipal, decimal ArrearsAmount, int DaysInArrears,
    DateOnly? NextDueDate, decimal NextDueAmount, DateOnly? DisbursementDate, int TermMonths, int InterestRateBps, DateTimeOffset AppliedAt);
public sealed record ExitLoanSettlement(string LoanNumber, decimal Principal, decimal Interest, Guid JournalEntryId);
public sealed record ExitSettlementResult(IReadOnlyList<ExitLoanSettlement> Loans, decimal TotalSettled);

public interface ILendingService
{
    Task<RepaymentResult> RepayAsync(RepaymentCommand command, CancellationToken ct);
    /// <summary>A member's loans with live balances — the self-service and exit views.</summary>
    Task<IReadOnlyList<MemberLoanSnapshot>> GetMemberLoansAsync(Guid memberId, CancellationToken ct);
    /// <summary>Pays off every active loan from the member's BOSA deposits (principal plus interest due to date; future interest waived). Fails if deposits do not cover the payoff or the member still guarantees others.</summary>
    Task<ExitSettlementResult> SettleOnExitAsync(Guid memberId, Guid byUserId, CancellationToken ct);
    Task<MemberLoanExposure> GetMemberExposureAsync(Guid memberId, CancellationToken ct);
    /// <summary>NPL aging and provisioning as of a date, plus outstanding per member (large-exposure reporting).</summary>
    Task<PortfolioQualitySnapshot> GetPortfolioQualityAsync(DateOnly asOf, CancellationToken ct);
}
