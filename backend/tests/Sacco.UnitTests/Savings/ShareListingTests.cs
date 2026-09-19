using Sacco.Modules.Savings.Domain;
using Sacco.Shared.Domain;
using Shouldly;

namespace Sacco.UnitTests.Savings;

public class ShareListingTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Seller = Guid.NewGuid();
    private static readonly Guid Buyer = Guid.NewGuid();
    private static readonly Guid ListedByUser = Guid.NewGuid();
    private static readonly Guid ClaimedByUser = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 9, 0, 0, TimeSpan.Zero);

    private static ShareListing Listing(decimal amount = 500m) =>
        ShareListing.Create(Guid.NewGuid(), Tenant, Seller, "M00001-SH", "M00001-FO", amount, ListedByUser, Now);

    [Fact]
    public void Amount_must_be_a_positive_round_figure()
    {
        Should.Throw<DomainRuleException>(() => ShareListing.Create(Guid.NewGuid(), Tenant, Seller, "M00001-SH", "M00001-FO", 0m, ListedByUser, Now))
            .Code.ShouldBe("savings.shares.listing.amount_invalid");
        Should.Throw<DomainRuleException>(() => ShareListing.Create(Guid.NewGuid(), Tenant, Seller, "M00001-SH", "M00001-FO", -100m, ListedByUser, Now))
            .Code.ShouldBe("savings.shares.listing.amount_invalid");
        Should.Throw<DomainRuleException>(() => ShareListing.Create(Guid.NewGuid(), Tenant, Seller, "M00001-SH", "M00001-FO", 100.005m, ListedByUser, Now))
            .Code.ShouldBe("savings.shares.listing.amount_invalid");
    }

    [Fact]
    public void A_member_cannot_claim_their_own_listing()
    {
        var listing = Listing();
        Should.Throw<DomainRuleException>(() => listing.Claim(Seller, "M00001-SH2", "M00001-FO", ClaimedByUser, Now))
            .Code.ShouldBe("savings.shares.listing.self_purchase");
    }

    [Fact]
    public void Only_an_open_listing_can_be_claimed()
    {
        var listing = Listing();
        listing.Claim(Buyer, "M00002-SH", "M00002-FO", ClaimedByUser, Now);
        Should.Throw<DomainRuleException>(() => listing.Claim(Guid.NewGuid(), "M00003-SH", "M00003-FO", Guid.NewGuid(), Now))
            .Code.ShouldBe("savings.shares.listing.not_open");
    }

    [Fact]
    public void Approval_requires_the_listing_to_be_pending_and_the_approver_to_be_neither_party()
    {
        var listing = Listing();
        Should.Throw<DomainRuleException>(() => listing.Approve(Approver, "SHARE-XFER:1", Now)).Code.ShouldBe("savings.shares.listing.not_pending");

        listing.Claim(Buyer, "M00002-SH", "M00002-FO", ClaimedByUser, Now);
        Should.Throw<MakerCheckerViolationException>(() => listing.Approve(ListedByUser, "SHARE-XFER:1", Now));
        Should.Throw<MakerCheckerViolationException>(() => listing.Approve(ClaimedByUser, "SHARE-XFER:1", Now));

        listing.Approve(Approver, "SHARE-XFER:1", Now);
        listing.Status.ShouldBe(ShareListingStatus.Approved);
        listing.JournalReference.ShouldBe("SHARE-XFER:1");
    }

    [Fact]
    public void Rejection_requires_a_reason_and_a_pending_listing()
    {
        var listing = Listing();
        listing.Claim(Buyer, "M00002-SH", "M00002-FO", ClaimedByUser, Now);
        Should.Throw<DomainRuleException>(() => listing.Reject(Approver, "  ", Now)).Code.ShouldBe("savings.shares.listing.reason_required");
        listing.Reject(Approver, "Buyer not in good standing", Now);
        listing.Status.ShouldBe(ShareListingStatus.Rejected);
        listing.RejectionReason.ShouldBe("Buyer not in good standing");
        Should.Throw<DomainRuleException>(() => listing.Reject(Approver, "again", Now)).Code.ShouldBe("savings.shares.listing.not_pending");
    }

    [Fact]
    public void Only_the_seller_can_cancel_and_only_while_open()
    {
        var listing = Listing();
        Should.Throw<DomainRuleException>(() => listing.Cancel(Buyer)).Code.ShouldBe("savings.shares.listing.not_owner");

        listing.Cancel(Seller);
        listing.Status.ShouldBe(ShareListingStatus.Cancelled);

        var claimed = Listing();
        claimed.Claim(Buyer, "M00002-SH", "M00002-FO", ClaimedByUser, Now);
        Should.Throw<DomainRuleException>(() => claimed.Cancel(Seller)).Code.ShouldBe("savings.shares.listing.not_open");
    }
}
