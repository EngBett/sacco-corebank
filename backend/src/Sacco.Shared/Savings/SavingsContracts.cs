namespace Sacco.Shared.Savings;

public enum ProductKind
{
    /// <summary>FOSA on-demand current/savings account.</summary>
    FosaCurrent = 1,
    /// <summary>BOSA non-withdrawable deposits (notice period; back loan eligibility).</summary>
    BosaDeposit = 2,
    Shares = 3,
    FixedDeposit = 4,
}

public enum DepositChannel
{
    Cash = 1,
    MPesa = 2,
    AirtelMoney = 3,
    BankTransfer = 4,
    /// <summary>Employer payroll check-off remittance.</summary>
    CheckOff = 5,
    /// <summary>Internal transfer from another account of the same member (e.g. loan disbursement into FOSA).</summary>
    Internal = 6,
}

public sealed record DepositCommand(
    string AccountNumber,
    decimal Amount,
    DepositChannel Channel,
    /// <summary>Idempotency key, e.g. the M-Pesa receipt number or teller receipt. Unique per tenant.</summary>
    string Reference,
    string? Narrative,
    Guid ByUserId);

public sealed record DepositResult(Guid JournalEntryId, string JournalReference, string AccountNumber, decimal Amount, decimal NewBalance);

public sealed record MemberSavingsSummary(
    Guid MemberId,
    decimal BosaDeposits,
    decimal Shares,
    decimal FosaBalance,
    decimal FixedDeposits,
    /// <summary>Distinct months in which a BOSA deposit was credited — used by loan eligibility rules.</summary>
    int MonthsWithContributions,
    DateOnly? FirstContributionDate,
    string? FosaAccountNumber,
    string? BosaDepositAccountNumber,
    string? SharesAccountNumber);

public sealed record WithdrawalPayoutInfo(Guid Id, string AccountNumber, Guid MemberId, decimal Amount, decimal Fee, string Channel, string? Destination, bool IsApproved, bool IsPaid);

/// <summary>Savings module's public surface for Lending and Payments.</summary>
public interface ISavingsService
{
    Task<DepositResult> DepositAsync(DepositCommand command, CancellationToken ct);
    Task<MemberSavingsSummary> GetMemberSummaryAsync(Guid memberId, CancellationToken ct);
    /// <summary>An approved withdrawal that a payment provider should pay out (mobile money / bank).</summary>
    Task<WithdrawalPayoutInfo?> GetWithdrawalForPayoutAsync(Guid withdrawalId, CancellationToken ct);
    /// <summary>Records a successful provider payout: posts the ledger movement and marks the withdrawal paid. Idempotent per withdrawal.</summary>
    Task CompleteWithdrawalPayoutAsync(Guid withdrawalId, string providerReference, Guid byUserId, CancellationToken ct);
    /// <summary>Provider payout failed: the withdrawal stays approved (funds still held) so it can be retried or rejected.</summary>
    Task FailWithdrawalPayoutAsync(Guid withdrawalId, string reason, CancellationToken ct);
}
