using Sacco.Shared.Domain;

namespace Sacco.Shared.Ledger;

/// <summary>Kinds of member-facing sub-ledger accounts. Product rules live in Savings/Lending; balances live here.</summary>
public enum LedgerAccountKind
{
    /// <summary>FOSA on-demand transactional account.</summary>
    Current = 1,
    /// <summary>BOSA non-withdrawable / notice-period deposits.</summary>
    Savings = 2,
    /// <summary>Member share capital.</summary>
    Shares = 3,
    FixedDeposit = 4,
    Loan = 5,
}

public enum LedgerAccountStatus
{
    Active = 1,
    Dormant = 2,
    Frozen = 3,
    Closed = 4,
}

/// <summary>One line of a balanced posting. Segment is explicit and must match the GL account's tag (never inferred).</summary>
public sealed record PostingLine(
    string GlAccountCode,
    Segment Segment,
    EntryDirection Direction,
    decimal Amount,
    string? LedgerAccountNumber = null,
    string? Narrative = null);

/// <summary>
/// A request from another module to post a balanced journal directly (system posting).
/// The calling module is responsible for its own governance (e.g. loan disbursement has
/// already passed maker-checker in Lending before it reaches the ledger).
/// </summary>
public sealed record PostingRequest(
    string Reference,          // unique per tenant — idempotency key, e.g. "LOAN-DISB:LN-000123"
    string Description,
    DateOnly ValueDate,
    string Source,             // originating module/channel, e.g. "Savings", "MPesa", "Seed"
    Guid PostedByUserId,
    IReadOnlyList<PostingLine> Lines,
    /// <summary>Office the posting belongs to (ADR 0018). Null falls back to the acting user's branch.</summary>
    Guid? BranchId = null);

public sealed record PostingResult(Guid JournalEntryId, string Reference, decimal TotalAmount);

public sealed record OpenLedgerAccountRequest(
    string AccountNumber,
    Guid MemberId,
    string ControlGlAccountCode,
    Segment Segment,
    LedgerAccountKind Kind,
    string ProductCode,
    Guid OpenedByUserId);

public sealed record LedgerAccountSnapshot(
    Guid Id,
    string AccountNumber,
    Guid MemberId,
    string ControlGlAccountCode,
    Segment Segment,
    LedgerAccountKind Kind,
    LedgerAccountStatus Status,
    string ProductCode,
    decimal Balance,
    decimal HeldAmount,
    decimal AvailableBalance,
    DateTimeOffset OpenedAt);

public sealed record GlAccountSnapshot(string Code, string Name, string Category, Segment Segment, bool IsPostable, bool IsControlAccount, bool IsActive);

public sealed record StatementLineSnapshot(DateOnly ValueDate, string Reference, string Description, string? Narrative, EntryDirection Direction, decimal Amount, decimal RunningBalance);
public sealed record AccountStatementSnapshot(string AccountNumber, decimal OpeningBalance, decimal ClosingBalance, IReadOnlyList<StatementLineSnapshot> Lines);

public sealed record TrialBalanceLineSnapshot(string Code, string Name, string Category, Segment Segment, EntryDirection NormalBalance, bool IsControlAccount, decimal Debit, decimal Credit, decimal Balance);
public sealed record TrialBalanceSnapshot(Segment? Segment, DateOnly AsOf, IReadOnlyList<TrialBalanceLineSnapshot> Lines, decimal TotalDebits, decimal TotalCredits, bool IsBalanced);
public sealed record GlActivitySnapshot(string Code, string Name, string Category, Segment Segment, EntryDirection NormalBalance, decimal Debit, decimal Credit);
public sealed record LedgerReconciliationSnapshot(bool IsClean, int GlAccountsChecked, int ControlAccountsChecked, IReadOnlyList<string> Issues);

/// <summary>
/// The Ledger module's public surface for other modules. Implemented in Sacco.Modules.Ledger;
/// consumers (Savings, Lending, Payments) depend on this interface only — never on Ledger's DbContext.
/// </summary>
public interface ILedgerService
{
    Task<PostingResult> PostAsync(PostingRequest request, CancellationToken ct);
    Task<LedgerAccountSnapshot> OpenAccountAsync(OpenLedgerAccountRequest request, CancellationToken ct);
    Task<LedgerAccountSnapshot?> FindAccountAsync(string accountNumber, CancellationToken ct);
    Task<IReadOnlyList<LedgerAccountSnapshot>> GetMemberAccountsAsync(Guid memberId, CancellationToken ct);
    Task SetAccountStatusAsync(string accountNumber, LedgerAccountStatus status, Guid byUserId, CancellationToken ct);
    Task<AccountStatementSnapshot?> GetStatementAsync(string accountNumber, DateOnly from, DateOnly to, CancellationToken ct);
    /// <summary>Running balance of a GL account, signed toward its normal side.</summary>
    Task<decimal> GetGlBalanceAsync(string glAccountCode, CancellationToken ct);
    Task<GlAccountSnapshot?> FindGlAccountAsync(string glAccountCode, CancellationToken ct);
    /// <summary>Trial balance computed from posted lines (not running balances) as of a value date.</summary>
    Task<TrialBalanceSnapshot> GetTrialBalanceAsync(Segment? segment, DateOnly asOf, CancellationToken ct);
    /// <summary>Debit/credit activity per postable GL account over a value-date range.</summary>
    Task<IReadOnlyList<GlActivitySnapshot>> GetGlActivityAsync(DateOnly from, DateOnly to, Segment? segment, CancellationToken ct);
    Task<LedgerReconciliationSnapshot> ReconcileAsync(CancellationToken ct);

    /// <summary>Places a lien (e.g. guarantor commitment) reducing available balance. Atomic; fails if unavailable.</summary>
    Task PlaceHoldAsync(string accountNumber, decimal amount, string reason, CancellationToken ct);
    Task ReleaseHoldAsync(string accountNumber, decimal amount, string reason, CancellationToken ct);
}
