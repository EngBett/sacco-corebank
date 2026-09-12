using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Savings.Domain;

public enum WithdrawalStatus { PendingApproval = 1, Approved = 2, Paid = 3, Rejected = 4, Cancelled = 5 }
public enum PayoutChannel { Cash = 1, MPesa = 2, AirtelMoney = 3, BankTransfer = 4 }

/// <summary>
/// A member withdrawal. Teller cash withdrawals within the product's teller limit are paid in one
/// step; everything else is maker-checker (requester ≠ approver) and, for notice products, cannot
/// be paid before the notice period ends. The amount plus fee is held on the ledger account while pending.
/// </summary>
public class WithdrawalRequest : TenantEntity
{
    private WithdrawalRequest() { }

    public string AccountNumber { get; private set; } = string.Empty;
    public Guid MemberId { get; private set; }
    public decimal Amount { get; private set; }
    public decimal Fee { get; private set; }
    public PayoutChannel Channel { get; private set; }
    /// <summary>Destination for non-cash payouts (phone number / bank account).</summary>
    public string? PayoutDestination { get; private set; }
    public WithdrawalStatus Status { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateOnly NoticeExpiresOn { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? PaidByUserId { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public string? JournalReference { get; private set; }
    public string? RejectionReason { get; private set; }
    public string? Narrative { get; private set; }
    public decimal TotalDebit => Amount + Fee;

    public static WithdrawalRequest Create(Guid id, Guid tenantId, SavingsAccount account, SavingsProduct product, decimal amount, PayoutChannel channel, string? destination, string? narrative, Guid requestedBy, DateTimeOffset now, DateOnly today)
    {
        if (!product.AllowsWithdrawals) throw new DomainRuleException("savings.withdrawal.not_allowed", $"{product.Name} does not allow withdrawals.");
        if (account.Status != SavingsAccountStatus.Active) throw new DomainRuleException("savings.account.not_active", $"{account.AccountNumber} is {account.Status}.");
        if (amount <= 0 || decimal.Round(amount, 2) != amount) throw new DomainRuleException("savings.withdrawal.amount_invalid", "Withdrawal amount must be a positive amount with at most two decimals.");
        if (channel != PayoutChannel.Cash && string.IsNullOrWhiteSpace(destination))
            throw new DomainRuleException("savings.withdrawal.destination_required", "A payout destination is required for non-cash withdrawals.");
        return new WithdrawalRequest
        {
            Id = id, TenantId = tenantId, AccountNumber = account.AccountNumber, MemberId = account.MemberId, Amount = amount, Fee = product.WithdrawalFee,
            Channel = channel, PayoutDestination = destination, Status = WithdrawalStatus.PendingApproval, RequestedByUserId = requestedBy, RequestedAt = now,
            NoticeExpiresOn = today.AddDays(product.WithdrawalNoticeDays), Narrative = narrative,
        };
    }

    /// <summary>True when a teller may pay this immediately: on-demand product, cash, within the teller limit.</summary>
    public bool QualifiesForTellerPayout(SavingsProduct product) =>
        product.WithdrawalNoticeDays == 0 && Channel == PayoutChannel.Cash && product.TellerWithdrawalLimit > 0 && Amount <= product.TellerWithdrawalLimit;

    public void Approve(Guid approver, DateTimeOffset now)
    {
        if (Status != WithdrawalStatus.PendingApproval) throw new DomainRuleException("savings.withdrawal.not_pending", $"Withdrawal is {Status}.");
        MakerChecker.EnsureDistinct(RequestedByUserId, approver, $"withdrawal {Id}");
        Status = WithdrawalStatus.Approved; ApprovedByUserId = approver; ApprovedAt = now;
    }

    /// <summary>Teller payout inside the limit — one step, recorded against the teller; no second person because the product policy pre-authorises it.</summary>
    public void ApproveByPolicy(DateTimeOffset now)
    {
        if (Status != WithdrawalStatus.PendingApproval) throw new DomainRuleException("savings.withdrawal.not_pending", $"Withdrawal is {Status}.");
        Status = WithdrawalStatus.Approved; ApprovedAt = now;
    }

    public void Reject(Guid by, string reason)
    {
        if (Status is not (WithdrawalStatus.PendingApproval or WithdrawalStatus.Approved)) throw new DomainRuleException("savings.withdrawal.not_pending", $"Withdrawal is {Status}.");
        MakerChecker.EnsureDistinct(RequestedByUserId, by, $"withdrawal {Id}");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainRuleException("savings.withdrawal.reason_required", "A rejection reason is required.");
        Status = WithdrawalStatus.Rejected; RejectionReason = reason.Trim();
    }

    public void Cancel()
    {
        if (Status is not (WithdrawalStatus.PendingApproval or WithdrawalStatus.Approved)) throw new DomainRuleException("savings.withdrawal.not_pending", $"Withdrawal is {Status}.");
        Status = WithdrawalStatus.Cancelled;
    }

    public void MarkPaid(Guid paidBy, string journalReference, DateTimeOffset now, DateOnly today)
    {
        if (Status != WithdrawalStatus.Approved) throw new DomainRuleException("savings.withdrawal.not_approved", $"Withdrawal is {Status}; it must be approved before payout.");
        if (today < NoticeExpiresOn) throw new DomainRuleException("savings.withdrawal.notice_not_expired", $"Notice period runs until {NoticeExpiresOn:yyyy-MM-dd}.");
        Status = WithdrawalStatus.Paid; PaidByUserId = paidBy; PaidAt = now; JournalReference = journalReference;
    }
}
