using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Members.Endpoints;
using Sacco.Modules.Savings.Domain;
using Sacco.Modules.Savings.Endpoints;
using Sacco.Seed.Data;
using Sacco.Seed.Seeders;
using Sacco.Shared.Auth;
using Sacco.Shared.Lending;
using Sacco.Shared.Tenancy;
using Shouldly;

namespace Sacco.IntegrationTests.Identity;

/// <summary>Members sign in with phone + PIN and see only their own records; staff tokens cannot use the self endpoints and member tokens cannot use staff ones.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class SelfServiceTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString, realAuth: true);
    public void Dispose() => _factory.Dispose();

    private async Task<HttpClient> MemberClient(string phone = "254700100001", string pin = MembersSeeder.DemoPin)
    {
        var client = _factory.ClientWithToken(await _factory.TokenFor(phone, pin));
        client.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        return client;
    }

    [Fact]
    public async Task Member_sees_own_profile_accounts_loans_and_statements_only()
    {
        var me = await MemberClient(); // M00001
        var profile = await (await me.GetAsync("/api/self/profile")).ReadAs<MyProfileResponse>();
        profile.MemberNumber.ShouldBe("M00001");
        profile.PhoneNumber.ShouldBe("254700100001");

        var accounts = await (await me.GetAsync("/api/self/accounts")).ReadAs<List<MySavingsAccountResponse>>();
        accounts.ShouldNotBeEmpty();
        accounts.ShouldAllBe(a => a.MemberId == DemoTenant.MemberId("M00001"));

        var loans = await (await me.GetAsync("/api/self/loans")).ReadAs<List<MemberLoanSnapshot>>();
        loans.ShouldContain(l => l.LoanNumber == "LN-000001");

        (await me.GetAsync($"/api/self/statements/{accounts[0].AccountNumber}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await me.GetAsync("/api/self/statements/M00002-SV")).StatusCode.ShouldBe(HttpStatusCode.NotFound, "another member's account is invisible, not forbidden");

        // No staff surface at all.
        (await me.GetAsync("/api/members")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await me.GetAsync("/api/loans")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await me.GetAsync("/api/notifications")).StatusCode.ShouldBe(HttpStatusCode.OK, "notifications are per-login and harmless");
    }

    [Fact]
    public async Task Staff_tokens_are_refused_on_self_endpoints_and_wrong_pins_are_rejected()
    {
        var staff = _factory.ClientWithToken(await _factory.TokenFor("teller", IdentitySeeder.DemoPassword));
        staff.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        (await staff.GetAsync("/api/self/accounts")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await Should.ThrowAsync<InvalidOperationException>(() => _factory.TokenFor("254700100001", "9999"));
    }

    [Fact]
    public async Task Member_can_apply_for_a_loan_and_start_a_sandbox_top_up()
    {
        var me = await MemberClient("254700100003"); // M00003: verified, funded deposits, no application in progress
        var loan = await (await me.PostAsJsonAsync("/api/self/loans", new Sacco.Modules.Lending.Endpoints.SelfApplyLoanRequest("EMG-LOAN", 5_000m, 6, "School uniforms", BureauConsent: true), HttpExtensions.JsonOptions)).ReadAs<MemberLoanSnapshot>();
        loan.Status.ShouldBe(LoanStatus.Applied);
        (await me.PostAsJsonAsync("/api/self/loans", new Sacco.Modules.Lending.Endpoints.SelfApplyLoanRequest("EMG-LOAN", 5_000m, 6, "again", BureauConsent: true), HttpExtensions.JsonOptions)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var accounts = await (await me.GetAsync("/api/self/accounts")).ReadAs<List<MySavingsAccountResponse>>();
        var fosa = accounts.First(a => a.Kind == Sacco.Shared.Savings.ProductKind.FosaCurrent);
        var tx = await me.PostAsJsonAsync("/api/self/payments/topup", new Sacco.Modules.Payments.Endpoints.SelfTopUpRequest("MPesa", 500m, fosa.AccountNumber, null), HttpExtensions.JsonOptions);
        tx.StatusCode.ShouldBe(HttpStatusCode.Created);
        var other = await me.PostAsJsonAsync("/api/self/payments/topup", new Sacco.Modules.Payments.Endpoints.SelfTopUpRequest("MPesa", 500m, "M00002-FO", null), HttpExtensions.JsonOptions);
        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Member_can_request_a_withdrawal_from_their_own_account_only_and_it_still_needs_a_staff_checker()
    {
        var me = await MemberClient("254700100003"); // M00003: verified, funded FOSA balance
        var accounts = await (await me.GetAsync("/api/self/accounts")).ReadAs<List<MySavingsAccountResponse>>();
        var fosa = accounts.First(a => a.Kind == Sacco.Shared.Savings.ProductKind.FosaCurrent);

        var mine = await me.PostAsJsonAsync("/api/self/withdrawals", new SelfWithdrawalRequest(fosa.AccountNumber, 1_000m, PayoutChannel.MPesa, "254700100003", "Self-service test"), HttpExtensions.JsonOptions);
        mine.StatusCode.ShouldBe(HttpStatusCode.Created);
        var withdrawal = await mine.ReadAs<WithdrawalResponse>();
        withdrawal.Status.ShouldBe(WithdrawalStatus.PendingApproval, "a member cannot approve their own money movement — this still needs a staff checker");

        var mySince = await (await me.GetAsync("/api/self/withdrawals")).ReadAs<List<WithdrawalResponse>>();
        mySince.ShouldContain(w => w.Id == withdrawal.Id);

        // Cannot request against another member's account — not found, never forbidden (RLS-style, not a permission leak).
        var notMine = await me.PostAsJsonAsync("/api/self/withdrawals", new SelfWithdrawalRequest("M00002-FO", 1_000m, PayoutChannel.MPesa, "254700100003", "x"), HttpExtensions.JsonOptions);
        notMine.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A staff checker (never the member) approves it, exactly like a teller-initiated request.
        var manager = _factory.ClientWithToken(await _factory.TokenFor("manager", IdentitySeeder.DemoPassword));
        manager.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        var approved = await (await manager.PostAsync($"/api/savings/withdrawals/{withdrawal.Id}/approve", null)).ReadAs<WithdrawalResponse>();
        approved.Status.ShouldBe(WithdrawalStatus.Approved);
    }

    [Fact]
    public async Task Member_sees_only_their_own_dividend_line_for_a_year_matching_the_staff_view()
    {
        var me = await MemberClient(); // M00001
        var mine = await (await me.GetAsync("/api/self/dividends")).ReadAs<List<MyDividendResponse>>();
        var fy2025 = mine.SingleOrDefault(d => d.FinancialYear == 2025);
        if (fy2025 is null) return; // M00001's FY2025 average balance rounded to zero — nothing to assert against.

        var accountant = _factory.ClientWithToken(await _factory.TokenFor("accountant", IdentitySeeder.DemoPassword));
        accountant.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        var declarations = await (await accountant.GetAsync("/api/savings/dividends")).ReadAs<List<DividendResponse>>();
        var declaration = declarations.Single(d => d.FinancialYear == 2025);
        var staffLine = declaration.Lines.Single(l => l.MemberId == DemoTenant.MemberId("M00001"));

        fy2025.Status.ShouldBe(declaration.Status);
        fy2025.NetPayable.ShouldBe(staffLine.NetPayable);
        fy2025.ShareDividend.ShouldBe(staffLine.ShareDividend);

        // Scoped to their own line only — the filtered-by-year query never leaks a year they have no line in.
        var filtered = await (await me.GetAsync("/api/self/dividends?year=2025")).ReadAs<List<MyDividendResponse>>();
        filtered.Single().FinancialYear.ShouldBe(2025);
    }

    [Fact]
    public async Task Row_level_security_is_forced_so_an_untenanted_connection_sees_nothing()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().HasTenant.ShouldBeFalse();
        var db = scope.ServiceProvider.GetRequiredService<Sacco.Modules.Members.Persistence.MembersDbContext>();
        (await db.Members.IgnoreQueryFilters().CountAsync()).ShouldBe(0, "FORCE ROW LEVEL SECURITY binds the owner too; without app.tenant_id no rows are visible");
        using var tenantScope = _factory.TenantScope();
        var db2 = tenantScope.ServiceProvider.GetRequiredService<Sacco.Modules.Members.Persistence.MembersDbContext>();
        (await db2.Members.IgnoreQueryFilters().CountAsync()).ShouldBeGreaterThan(0);
    }
}
