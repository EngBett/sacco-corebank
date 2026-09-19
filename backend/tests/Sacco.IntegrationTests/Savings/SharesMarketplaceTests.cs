using System.Net;
using System.Net.Http.Json;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Savings.Domain;
using Sacco.Modules.Savings.Endpoints;
using Sacco.Seed.Data;
using Sacco.Seed.Seeders;
using Shouldly;

namespace Sacco.IntegrationTests.Savings;

/// <summary>Member-to-member share capital marketplace (ADR 0008 follow-up): shares aren't withdrawable —
/// see <c>SavingsWorkflowTests.Cannot_withdraw_directly_from_a_shares_account</c> — so this is the only way
/// to exit share capital short of a full member exit. Listing→claim→approve moves shares and cash together.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class SharesMarketplaceTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString, realAuth: true);
    public void Dispose() => _factory.Dispose();

    private async Task<HttpClient> StaffClient(string userName, string password = IdentitySeeder.DemoPassword)
    {
        var client = _factory.ClientWithToken(await _factory.TokenFor(userName, password));
        client.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        return client;
    }

    private Task<HttpClient> Manager() => StaffClient("manager");

    private async Task<HttpClient> MemberClient(string memberNumber)
    {
        var phone = "2547001000" + memberNumber[^2..];
        var client = _factory.ClientWithToken(await _factory.TokenFor(phone, MembersSeeder.DemoPin));
        client.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        return client;
    }

    /// <summary>Read through the staff ledger view: a member's own FOSA balance is fee-gated in self-service (ADR 0015).</summary>
    private async Task<LedgerAccountSnapshotDto> Ledger(string number) => await (await (await Manager()).GetAsync($"/api/ledger/accounts/{number}")).ReadAs<LedgerAccountSnapshotDto>();

    [Fact]
    public async Task Listing_claim_and_approval_moves_shares_and_cash_between_the_two_members()
    {
        var seller = await MemberClient("M00003"); // healthy FOSA + share balance (seeded top-up)
        var buyer = await MemberClient("M00009");

        var created = await (await seller.PostAsJsonAsync("/api/self/shares-marketplace/listings", new CreateShareListingRequest(500m))).ReadAs<ShareListingResponse>();
        created.Status.ShouldBe(ShareListingStatus.Open);
        created.Amount.ShouldBe(500m);

        // Visible to another member's browse feed, invisible to the seller's own.
        var openForBuyer = await (await buyer.GetAsync("/api/self/shares-marketplace/listings/open")).ReadAs<List<ShareListingResponse>>();
        openForBuyer.ShouldContain(l => l.Id == created.Id);
        var openForSeller = await (await seller.GetAsync("/api/self/shares-marketplace/listings/open")).ReadAs<List<ShareListingResponse>>();
        openForSeller.ShouldNotContain(l => l.Id == created.Id);

        // The seller cannot buy their own listing.
        var selfBuy = await seller.PostAsync($"/api/self/shares-marketplace/listings/{created.Id}/claim", null);
        selfBuy.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await selfBuy.Content.ReadAsStringAsync()).ShouldContain("savings.shares.listing.self_purchase");

        var claimed = await (await buyer.PostAsync($"/api/self/shares-marketplace/listings/{created.Id}/claim", null)).ReadAs<ShareListingResponse>();
        claimed.Status.ShouldBe(ShareListingStatus.PendingApproval);
        claimed.BuyerMemberId.ShouldBe(DemoTenant.MemberId("M00009"));

        // A staff member without the checker permission is refused.
        (await (await StaffClient("teller")).PostAsync($"/api/savings/shares-marketplace/{created.Id}/approve", null))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var sellerSharesBefore = await Ledger(LedgerSeeder.SharesAccount("M00003"));
        var buyerSharesBefore = await Ledger(LedgerSeeder.SharesAccount("M00009"));
        var sellerFosaBefore = await Ledger(LedgerSeeder.FosaAccount("M00003"));
        var buyerFosaBefore = await Ledger(LedgerSeeder.FosaAccount("M00009"));

        var approved = await (await (await Manager()).PostAsync($"/api/savings/shares-marketplace/{created.Id}/approve", null)).ReadAs<ShareListingResponse>();
        approved.Status.ShouldBe(ShareListingStatus.Approved);

        (await Ledger(LedgerSeeder.SharesAccount("M00003"))).Balance.ShouldBe(sellerSharesBefore.Balance - 500m);
        (await Ledger(LedgerSeeder.SharesAccount("M00009"))).Balance.ShouldBe(buyerSharesBefore.Balance + 500m);
        (await Ledger(LedgerSeeder.FosaAccount("M00003"))).Balance.ShouldBe(sellerFosaBefore.Balance + 500m);
        (await Ledger(LedgerSeeder.FosaAccount("M00009"))).Balance.ShouldBe(buyerFosaBefore.Balance - 500m);
    }

    [Fact]
    public async Task Rejecting_a_claim_releases_both_holds_and_leaves_balances_untouched()
    {
        var seller = await MemberClient("M00004");
        var buyer = await MemberClient("M00007");

        var created = await (await seller.PostAsJsonAsync("/api/self/shares-marketplace/listings", new CreateShareListingRequest(300m))).ReadAs<ShareListingResponse>();
        await buyer.PostAsync($"/api/self/shares-marketplace/listings/{created.Id}/claim", null);

        var sellerSharesBefore = await Ledger(LedgerSeeder.SharesAccount("M00004"));
        var rejected = await (await (await Manager()).PostAsJsonAsync($"/api/savings/shares-marketplace/{created.Id}/reject", new ReasonDto("Buyer failed a compliance check"))).ReadAs<ShareListingResponse>();
        rejected.Status.ShouldBe(ShareListingStatus.Rejected);

        var sellerSharesAfter = await Ledger(LedgerSeeder.SharesAccount("M00004"));
        sellerSharesAfter.Balance.ShouldBe(sellerSharesBefore.Balance, "rejection must not move any money");
    }

    [Fact]
    public async Task Seller_can_cancel_their_own_open_listing_but_not_someone_elses()
    {
        var seller = await MemberClient("M00011");
        var stranger = await MemberClient("M00012");

        var created = await (await seller.PostAsJsonAsync("/api/self/shares-marketplace/listings", new CreateShareListingRequest(200m))).ReadAs<ShareListingResponse>();

        var strangerCancel = await stranger.PostAsync($"/api/self/shares-marketplace/listings/{created.Id}/cancel", null);
        strangerCancel.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await strangerCancel.Content.ReadAsStringAsync()).ShouldContain("savings.shares.listing.not_owner");

        var cancelled = await (await seller.PostAsync($"/api/self/shares-marketplace/listings/{created.Id}/cancel", null)).ReadAs<ShareListingResponse>();
        cancelled.Status.ShouldBe(ShareListingStatus.Cancelled);
    }
}

/// <summary>Just the balance out of the ledger account response — the rest of the DTO isn't relevant here.</summary>
public sealed record LedgerAccountSnapshotDto(decimal Balance);
