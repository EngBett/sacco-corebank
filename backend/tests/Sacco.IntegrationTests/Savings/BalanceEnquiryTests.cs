using System.Net;
using System.Net.Http.Json;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Identity.Endpoints;
using Sacco.Modules.Savings.Endpoints;
using Sacco.Seed.Data;
using Sacco.Seed.Seeders;
using Sacco.Shared.Ledger;
using Shouldly;

namespace Sacco.IntegrationTests.Savings;

/// <summary>
/// Balance-enquiry fees (ADR 0015): the seeded FOSA-CUR rule (KES 10) hides FOSA balances from self-service — accounts,
/// summary and statement — until the member pays for a reveal window. Staff views are never gated.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BalanceEnquiryTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString, realAuth: true);
    public void Dispose() => _factory.Dispose();

    private async Task<HttpClient> Client(string userName, string password)
    {
        var client = _factory.ClientWithToken(await _factory.TokenFor(userName, password));
        client.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        return client;
    }

    [Fact]
    public async Task A_fee_gated_balance_is_hidden_until_the_member_pays_once_for_a_reveal_window()
    {
        var member = await Client("254700100006", MembersSeeder.DemoPin); // M00006
        var manager = await Client("manager", IdentitySeeder.DemoPassword);
        var fosa = LedgerSeeder.FosaAccount("M00006");
        async Task<decimal> StaffBalance() => (await (await manager.GetAsync($"/api/ledger/accounts/{fosa}")).ReadAs<LedgerAccountSnapshot>()).Balance;

        var accounts = await (await member.GetAsync("/api/self/accounts")).ReadAs<List<MySavingsAccountResponse>>();
        var locked = accounts.Single(a => a.AccountNumber == fosa);
        locked.BalanceLocked.ShouldBeTrue();
        locked.Balance.ShouldBeNull();
        locked.BalanceEnquiryFee.ShouldBe(10m);
        accounts.Where(a => a.AccountNumber != fosa).ShouldAllBe(a => !a.BalanceLocked && a.Balance != null, "only FOSA-CUR carries the seeded fee");

        var summary = await (await member.GetAsync("/api/self/summary")).ReadAs<MySavingsSummaryResponse>();
        summary.HasLockedBalances.ShouldBeTrue();
        summary.FosaBalance.ShouldBeNull();
        summary.Shares.ShouldNotBeNull();

        var statement = await member.GetAsync($"/api/self/statements/{fosa}");
        statement.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, "running balances would leak the balance");
        (await statement.Content.ReadAsStringAsync()).ShouldContain("savings.balance.locked");

        var before = await StaffBalance();
        var key = Guid.NewGuid().ToString("N");
        var reveal = await (await member.PostAsJsonAsync($"/api/self/accounts/{fosa}/balance-enquiries", new RevealBalanceRequest(key))).ReadAs<RevealBalanceResponse>();
        reveal.Charged.ShouldBeTrue();
        reveal.Fee.ShouldBe(10m);
        reveal.VisibleUntil.ShouldNotBeNull();
        reveal.Account.BalanceLocked.ShouldBeFalse();
        reveal.Account.Balance.ShouldBe(before - 10m, "the fee comes off the FOSA account itself");

        var retry = await (await member.PostAsJsonAsync($"/api/self/accounts/{fosa}/balance-enquiries", new RevealBalanceRequest(key))).ReadAs<RevealBalanceResponse>();
        retry.Charged.ShouldBeFalse("the same idempotency key never charges twice");
        var anotherTap = await (await member.PostAsJsonAsync($"/api/self/accounts/{fosa}/balance-enquiries", new RevealBalanceRequest(Guid.NewGuid().ToString("N")))).ReadAs<RevealBalanceResponse>();
        anotherTap.Charged.ShouldBeFalse("the window is already open");
        (await StaffBalance()).ShouldBe(before - 10m);

        (await member.GetAsync($"/api/self/statements/{fosa}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await member.GetAsync("/api/self/summary")).ReadAs<MySavingsSummaryResponse>()).FosaBalance.ShouldNotBeNull();
    }

    [Fact]
    public async Task Free_accounts_reveal_without_charging_and_another_members_account_is_invisible()
    {
        var member = await Client("254700100006", MembersSeeder.DemoPin);
        var shares = LedgerSeeder.SharesAccount("M00006");
        var free = await (await member.PostAsJsonAsync($"/api/self/accounts/{shares}/balance-enquiries", new RevealBalanceRequest(Guid.NewGuid().ToString("N")))).ReadAs<RevealBalanceResponse>();
        free.Charged.ShouldBeFalse();
        free.Fee.ShouldBe(0m);

        (await member.PostAsJsonAsync($"/api/self/accounts/{LedgerSeeder.FosaAccount("M00002")}/balance-enquiries", new RevealBalanceRequest("x"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_member_can_re_check_their_pin_and_a_wrong_pin_is_an_answer_not_a_401()
    {
        var member = await Client("254700100011", MembersSeeder.DemoPin); // M00011
        var wrong = await member.PostAsJsonAsync("/api/self/auth/pin/verify", new VerifyPinRequest("0000"));
        wrong.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await wrong.Content.ReadFromJsonAsync<VerifyPinResponse>())!.Valid.ShouldBeFalse();

        var right = await (await member.PostAsJsonAsync("/api/self/auth/pin/verify", new VerifyPinRequest(MembersSeeder.DemoPin))).Content.ReadFromJsonAsync<VerifyPinResponse>();
        right!.Valid.ShouldBeTrue();
        right.LockedOut.ShouldBeFalse();

        var staff = await Client("teller", IdentitySeeder.DemoPassword);
        (await staff.PostAsJsonAsync("/api/self/auth/pin/verify", new VerifyPinRequest(MembersSeeder.DemoPin))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
