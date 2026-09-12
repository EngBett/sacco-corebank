using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Ledger.Domain;

/// <summary>
/// A member-facing sub-ledger account (savings, shares, FOSA current, fixed deposit, loan)
/// attached to a control GL account. Balance is "natural" positive: credit-positive for
/// deposit/share accounts, debit-positive for loan accounts, matching the control account's
/// normal side. <see cref="HeldAmount"/> is the sum of liens (guarantor commitments etc.).
/// </summary>
public class LedgerAccount : TenantEntity
{
    private LedgerAccount() { }

    public string AccountNumber { get; private set; } = string.Empty;
    /// <summary>Opaque reference to the Members module; the ledger never joins to member tables.</summary>
    public Guid MemberId { get; private set; }
    public Guid ControlGlAccountId { get; private set; }
    public Segment Segment { get; private set; }
    public LedgerAccountKind Kind { get; private set; }
    public LedgerAccountStatus Status { get; private set; }
    /// <summary>Opaque product code owned by Savings/Lending.</summary>
    public string ProductCode { get; private set; } = string.Empty;
    public string Currency { get; private set; } = "KES";
    public decimal Balance { get; private set; }
    public decimal HeldAmount { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public Guid OpenedByUserId { get; private set; }

    public decimal AvailableBalance => Balance - HeldAmount;

    public static LedgerAccount Open(Guid id, Guid tenantId, string accountNumber, Guid memberId, GlAccount control, LedgerAccountKind kind, string productCode, Guid openedByUserId, DateTimeOffset now)
    {
        if (!control.IsControlAccount)
            throw new DomainRuleException("ledger.account.not_control", $"GL account {control.Code} is not a control account.");
        if (!control.IsActive)
            throw new DomainRuleException("ledger.account.control_inactive", $"GL account {control.Code} is inactive.");
        if (string.IsNullOrWhiteSpace(accountNumber))
            throw new DomainRuleException("ledger.account.number_required", "Account number is required.");
        if (memberId == Guid.Empty)
            throw new DomainRuleException("ledger.account.member_required", "Member is required.");

        return new LedgerAccount
        {
            Id = id,
            TenantId = tenantId,
            AccountNumber = accountNumber.Trim(),
            MemberId = memberId,
            ControlGlAccountId = control.Id,
            Segment = control.Segment, // the sub-account inherits its control account's tag — explicitly stored, never inferred at read time
            Kind = kind,
            Status = LedgerAccountStatus.Active,
            ProductCode = productCode,
            OpenedAt = now,
            OpenedByUserId = openedByUserId,
        };
    }

    public void SetStatus(LedgerAccountStatus status, DateTimeOffset now)
    {
        if (Status == LedgerAccountStatus.Closed)
            throw new DomainRuleException("ledger.account.closed", $"Account {AccountNumber} is closed.");
        if (status == LedgerAccountStatus.Closed)
        {
            if (Balance != 0m || HeldAmount != 0m)
                throw new DomainRuleException("ledger.account.close_nonzero", $"Account {AccountNumber} cannot be closed with a non-zero balance or active holds.");
            ClosedAt = now;
        }
        Status = status;
    }
}
