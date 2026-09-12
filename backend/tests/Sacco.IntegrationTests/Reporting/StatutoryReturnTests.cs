using System.Net;
using System.Net.Http.Json;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Reporting.Application;
using Sacco.Modules.Reporting.Domain;
using Sacco.Modules.Reporting.Endpoints;
using Sacco.Seed.Data;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Shouldly;

namespace Sacco.IntegrationTests.Reporting;

/// <summary>Phase 6 exit criterion: a full statutory return package from seeded data that reconciles against the ledger to the cent.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class StatutoryReturnTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    public void Dispose() => _factory.Dispose();

    private HttpClient Accountant => _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Reporting.View, Permissions.Reporting.StatutoryGenerate);
    private HttpClient Compliance => _factory.ClientAs(DemoTenant.Users.ComplianceOfficer, Permissions.Reporting.View, Permissions.Reporting.StatutorySubmit);

    [Fact]
    public async Task Financial_positions_balance_per_segment_and_consolidate()
    {
        var all = await (await Accountant.GetAsync("/api/reporting/financial-position")).ReadAs<FinancialPosition>();
        var fosa = await (await Accountant.GetAsync("/api/reporting/financial-position?segment=Fosa")).ReadAs<FinancialPosition>();
        var bosa = await (await Accountant.GetAsync("/api/reporting/financial-position?segment=Bosa")).ReadAs<FinancialPosition>();
        all.Balances.ShouldBeTrue($"{all.TotalAssets} vs {all.TotalLiabilitiesAndEquity}");
        fosa.Balances.ShouldBeTrue();
        bosa.Balances.ShouldBeTrue();
        (fosa.TotalAssets + bosa.TotalAssets - all.InterSegmentNetted).ShouldBe(all.TotalAssets);
        all.Assets.Lines.ShouldNotContain(l => l.Code == "1800", "clearing accounts are netted on consolidation");
        bosa.Assets.Lines.ShouldContain(l => l.Code == "1800");
        all.Assets.Lines.Single(l => l.Code == "1900").Amount.ShouldBeLessThanOrEqualTo(0, "loan-loss provision is a contra asset");
    }

    [Fact]
    public async Task Ratios_are_computed_against_configured_thresholds()
    {
        var capital = await (await Accountant.GetAsync("/api/reporting/capital-adequacy")).ReadAs<CapitalAdequacy>();
        capital.CoreCapital.ShouldBe(capital.ShareCapital + capital.InstitutionalCapital);
        capital.Ratios.Count.ShouldBe(3);
        capital.Ratios.ShouldAllBe(r => r.MinimumBps > 0);
        var liquidity = await (await Accountant.GetAsync("/api/reporting/liquidity")).ReadAs<LiquidityPosition>();
        liquidity.LiquidAssets.ShouldBeGreaterThan(0);
        liquidity.Ratio.MinimumBps.ShouldBe(1500);
        var exposures = await (await Accountant.GetAsync("/api/reporting/large-exposures")).ReadAs<LargeExposureReport>();
        exposures.ThresholdBps.ShouldBe(2500);
        var income = await (await Accountant.GetAsync("/api/reporting/income-statement?from=2025-09-01&to=2026-09-11")).ReadAs<IncomeStatement>();
        income.Income.Total.ShouldBeGreaterThan(0);
        income.Expenses.Total.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Generated_return_reconciles_to_the_ledger_and_submission_is_maker_checker()
    {
        var periodEnd = new DateOnly(2026, 9, 11);
        var created = await Accountant.PostAsJsonAsync("/api/reporting/statutory-returns", new GenerateReturnRequest(new DateOnly(2026, 7, 1), periodEnd));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var detail = await created.ReadAs<StatutoryReturnDetail>();
        detail.Summary.IsReconciled.ShouldBeTrue(detail.Summary.ReconciliationNotes);
        detail.Package.Reconciliation.Failures.ShouldBeEmpty();
        detail.Package.Reconciliation.Checks.Count.ShouldBeGreaterThanOrEqualTo(8);
        detail.Package.ConsolidatedPosition.TotalAssets.ShouldBe(detail.Package.ConsolidatedPosition.TotalLiabilitiesAndEquity);
        detail.Package.PortfolioQuality.Buckets.ShouldContain(b => b.Name == "Loss" && b.Loans > 0);
        detail.Package.OpenItems.ShouldNotBeEmpty("SASRA confirmations are surfaced, not hidden");

        (await Accountant.PostAsJsonAsync("/api/reporting/statutory-returns", new GenerateReturnRequest(new DateOnly(2026, 7, 1), periodEnd))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var self = _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Reporting.StatutorySubmit);
        (await self.PostAsJsonAsync($"/api/reporting/statutory-returns/{detail.Summary.Id}/submit", new SubmitReturnRequest("SASRA-ACK-1"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var submitted = await (await Compliance.PostAsJsonAsync($"/api/reporting/statutory-returns/{detail.Summary.Id}/submit", new SubmitReturnRequest("SASRA-ACK-1"))).ReadAs<StatutoryReturnSummary>();
        submitted.Status.ShouldBe(ReturnStatus.Submitted);
        submitted.SubmittedByUserId.ShouldBe(DemoTenant.Users.ComplianceOfficer);

        var stored = await (await Compliance.GetAsync($"/api/reporting/statutory-returns/{detail.Summary.Id}")).ReadAs<StatutoryReturnDetail>();
        stored.Package.CapitalAdequacy.CoreCapital.ShouldBe(detail.Package.CapitalAdequacy.CoreCapital);
    }

    [Fact]
    public async Task Seeded_quarterly_return_exists_and_reconciles()
    {
        var list = await (await Compliance.GetAsync("/api/reporting/statutory-returns")).ReadAs<List<StatutoryReturnSummary>>();
        var seeded = list.Single(r => r.PeriodEnd == new DateOnly(2026, 6, 30));
        seeded.Status.ShouldBe(ReturnStatus.Generated);
        seeded.IsReconciled.ShouldBeTrue(seeded.ReconciliationNotes);
    }
}
