using System.Net;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Ledger.Application;
using Sacco.Seed.Data;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Shouldly;

namespace Sacco.IntegrationTests.Ledger;

[Collection(DatabaseCollection.Name)]
public sealed class TrialBalanceTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData(null)]
    [InlineData(Segment.Fosa)]
    [InlineData(Segment.Bosa)]
    public async Task Seeded_trial_balance_balances_for_consolidated_and_each_segment(Segment? segment)
    {
        var client = _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Ledger.View);
        var url = "/api/ledger/trial-balance" + (segment is null ? "" : $"?segment={segment}");
        var tb = await (await client.GetAsync(url)).ReadAs<TrialBalance>();

        tb.IsBalanced.ShouldBeTrue($"debits {tb.TotalDebits} vs credits {tb.TotalCredits}");
        tb.TotalDebits.ShouldBeGreaterThan(0);
        tb.Rows.ShouldAllBe(r => segment == null || r.Segment == segment);
        tb.Rows.ShouldContain(r => r.Balance != 0);
    }

    [Fact]
    public async Task Segment_trial_balances_sum_to_consolidated()
    {
        var client = _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Ledger.View);
        var all = await (await client.GetAsync("/api/ledger/trial-balance")).ReadAs<TrialBalance>();
        var fosa = await (await client.GetAsync("/api/ledger/trial-balance?segment=Fosa")).ReadAs<TrialBalance>();
        var bosa = await (await client.GetAsync("/api/ledger/trial-balance?segment=Bosa")).ReadAs<TrialBalance>();
        (fosa.TotalDebits + bosa.TotalDebits).ShouldBe(all.TotalDebits);
        (fosa.TotalCredits + bosa.TotalCredits).ShouldBe(all.TotalCredits);
    }

    [Fact]
    public async Task Inter_segment_clearing_accounts_mirror_each_other()
    {
        var client = _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Ledger.View);
        var tb = await (await client.GetAsync("/api/ledger/trial-balance")).ReadAs<TrialBalance>();
        var dueFrom = tb.Rows.Single(r => r.Code == "1800").Balance;
        var dueTo = tb.Rows.Single(r => r.Code == "2800").Balance;
        dueFrom.ShouldBe(dueTo);
        dueFrom.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Running_balances_reconcile_to_journal_lines_and_control_accounts()
    {
        var client = _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Ledger.View);
        var report = await (await client.GetAsync("/api/ledger/reconciliation")).ReadAs<ReconciliationReport>();
        report.Issues.ShouldBeEmpty();
        report.IsClean.ShouldBeTrue();
        report.ControlAccountsChecked.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Viewing_requires_permission_and_tenant()
    {
        var noPerm = _factory.ClientAs(DemoTenant.Users.Teller);
        (await noPerm.GetAsync("/api/ledger/trial-balance")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var anonymous = _factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        (await anonymous.GetAsync("/api/ledger/trial-balance")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var noTenant = _factory.CreateClient();
        (await noTenant.GetAsync("/api/ledger/trial-balance")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
