using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Savings.Domain;

public enum ShareListingStatus { Open = 1, PendingApproval = 2, Approved = 3, Rejected = 4, Cancelled = 5 }

/// <summary>
/// A member offering part of their share capital for sale to another member (ADR 0008 follow-up:
/// shares are not withdrawable — see <c>savings.shares.no_direct_withdrawal</c> — so exiting share
/// capital happens by transfer/sale instead). Transfers always happen at par (the seller's own book
/// value, KES for KES — no premium/discount) and are maker-checker: the seller lists, a different
/// member claims it, and a staff member with <c>savings.shares_marketplace.approve</c> must approve
/// before shares and cash actually move.
/// </summary>
public class ShareListing : TenantEntity
{
    private ShareListing() { }

    public Guid SellerMemberId { get; private set; }
    public string SellerSharesAccountNumber { get; private set; } = string.Empty;
    public string SellerFosaAccountNumber { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public ShareListingStatus Status { get; private set; }
    public Guid ListedByUserId { get; private set; }
    public DateTimeOffset ListedAt { get; private set; }

    public Guid? BuyerMemberId { get; private set; }
    public string? BuyerSharesAccountNumber { get; private set; }
    public string? BuyerFosaAccountNumber { get; private set; }
    public Guid? ClaimedByUserId { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }

    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public string? JournalReference { get; private set; }

    public static ShareListing Create(Guid id, Guid tenantId, Guid sellerMemberId, string sellerSharesAccountNumber, string sellerFosaAccountNumber, decimal amount, Guid listedByUserId, DateTimeOffset now)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new DomainRuleException("savings.shares.listing.amount_invalid", "List an amount greater than zero, in whole cents.");
        return new ShareListing
        {
            Id = id, TenantId = tenantId, SellerMemberId = sellerMemberId, SellerSharesAccountNumber = sellerSharesAccountNumber, SellerFosaAccountNumber = sellerFosaAccountNumber,
            Amount = amount, Status = ShareListingStatus.Open, ListedByUserId = listedByUserId, ListedAt = now,
        };
    }

    public void Claim(Guid buyerMemberId, string buyerSharesAccountNumber, string buyerFosaAccountNumber, Guid claimedByUserId, DateTimeOffset now)
    {
        if (Status != ShareListingStatus.Open) throw new DomainRuleException("savings.shares.listing.not_open", "This listing is no longer available.");
        if (buyerMemberId == SellerMemberId) throw new DomainRuleException("savings.shares.listing.self_purchase", "You cannot buy your own listing.");
        BuyerMemberId = buyerMemberId; BuyerSharesAccountNumber = buyerSharesAccountNumber; BuyerFosaAccountNumber = buyerFosaAccountNumber;
        ClaimedByUserId = claimedByUserId; ClaimedAt = now; Status = ShareListingStatus.PendingApproval;
    }

    public void Approve(Guid approver, string journalReference, DateTimeOffset now)
    {
        if (Status != ShareListingStatus.PendingApproval) throw new DomainRuleException("savings.shares.listing.not_pending", $"Listing is {Status}.");
        MakerChecker.EnsureDistinct(ListedByUserId, approver, $"share listing {Id}");
        MakerChecker.EnsureDistinct(ClaimedByUserId!.Value, approver, $"share listing {Id}");
        Status = ShareListingStatus.Approved; DecidedByUserId = approver; DecidedAt = now; JournalReference = journalReference;
    }

    public void Reject(Guid by, string reason, DateTimeOffset now)
    {
        if (Status != ShareListingStatus.PendingApproval) throw new DomainRuleException("savings.shares.listing.not_pending", $"Listing is {Status}.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainRuleException("savings.shares.listing.reason_required", "A rejection reason is required.");
        Status = ShareListingStatus.Rejected; DecidedByUserId = by; DecidedAt = now; RejectionReason = reason.Trim();
    }

    public void Cancel(Guid bySellerMemberId)
    {
        if (bySellerMemberId != SellerMemberId) throw new DomainRuleException("savings.shares.listing.not_owner", "Only the seller can cancel this listing.");
        if (Status != ShareListingStatus.Open) throw new DomainRuleException("savings.shares.listing.not_open", "Only an open listing can be cancelled.");
        Status = ShareListingStatus.Cancelled;
    }
}
