using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Lending.Application;
using Sacco.Modules.Lending.Domain;
using Sacco.Modules.Lending.Endpoints;
using Sacco.Modules.Lending.Persistence;
using Sacco.Seed.Data;
using Sacco.Shared.Auth;
using Sacco.Shared.Http;
using Sacco.Shared.Lending;
using Shouldly;

namespace Sacco.IntegrationTests.Lending;

/// <summary>
/// Every application is scored automatically and re-scored at appraisal; the sandbox bureau is deterministic
/// (national ID ending 0 = listed, 9 = unavailable); the scorecard is policy behind its own permission; and a
/// score never changes a loan's status — the maker-checker workflow still decides.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CreditScoringTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    public void Dispose() => _factory.Dispose();

    private async Task<Loan> SeededLoan(string memberNumber, LoanStatus? status = null)
    {
        using var scope = _factory.TenantScope();
        var db = scope.ServiceProvider.GetRequiredService<LendingDbContext>();
        var q = db.Loans.AsNoTracking().Where(l => l.MemberId == DemoTenant.MemberId(memberNumber));
        if (status is { } s) q = q.Where(l => l.Status == s);
        return await q.OrderByDescending(l => l.AppliedAt).FirstAsync();
    }

    [Fact]
    public async Task Seeded_applications_carry_an_application_score_and_appraised_loans_a_second_one()
    {
        var viewer = _factory.ClientAs(DemoTenant.Users.LoanOfficer, Permissions.Loans.View);

        // M00007's emergency loan is seeded as Applied: exactly one score, at the application stage. ID ends in 7 → bureau clear.
        var applied = await SeededLoan("M00007", LoanStatus.Applied);
        // History is newest first; other tests in this class may add manual recomputations, so look at the oldest entry.
        var history = await (await viewer.GetAsync($"/api/loans/{applied.Id}/score/history")).ReadAs<List<CreditScoreResponse>>();
        history.ShouldNotBeEmpty();
        var first = history[^1];
        first.Stage.ShouldBe(ScoreStage.Application);
        first.BureauStatus.ShouldBe(BureauStatus.Clear);
        first.Factors.Select(f => f.Key).ShouldBe(Scorecard.DefaultFactors.Select(f => f.Key), ignoreOrder: true);
        history.ShouldAllBe(h => h.Stage != ScoreStage.Appraisal, "an Applied loan has not been appraised");

        // M00009's development loan was appraised: application + appraisal scores. ID ends in 9 → bureau unavailable → never a plain Approve.
        var appraised = await SeededLoan("M00009", LoanStatus.PendingApproval);
        var latest = await (await viewer.GetAsync($"/api/loans/{appraised.Id}/score")).ReadAs<CreditScoreResponse>();
        latest.Stage.ShouldBe(ScoreStage.Appraisal);
        latest.BureauStatus.ShouldBe(BureauStatus.Unavailable);
        latest.Recommendation.ShouldNotBe(ScoreRecommendation.Approve);
        var appraisedHistory = await (await viewer.GetAsync($"/api/loans/{appraised.Id}/score/history")).ReadAs<List<CreditScoreResponse>>();
        appraisedHistory[^1].Stage.ShouldBe(ScoreStage.Application);
        appraisedHistory.Count(h => h.Stage == ScoreStage.Appraisal).ShouldBe(1);

        // The summary rides along on the loan itself and on the list.
        var loan = await (await viewer.GetAsync($"/api/loans/{appraised.Id}")).ReadAs<LoanResponse>();
        loan.CreditScore.ShouldNotBeNull();
        loan.CreditScore!.Score.ShouldBe(latest.Score);
        var list = await (await viewer.GetAsync("/api/loans?status=PendingApproval&pageSize=50")).ReadAs<PagedResult<LoanListItem>>();
        list.Items.Single(l => l.Id == appraised.Id).CreditScore!.Grade.ShouldBe(latest.Grade);
    }

    [Fact]
    public async Task Bureau_listing_yields_a_decline_recommendation_without_touching_the_loan()
    {
        // M00010's national ID ends in 0 → sandbox bureau lists him. His seeded loan is Active, so score a fresh application through the service.
        using var scope = _factory.TenantScope();
        var scoring = scope.ServiceProvider.GetRequiredService<CreditScoringService>();
        var db = scope.ServiceProvider.GetRequiredService<LendingDbContext>();
        var applied = await SeededLoan("M00007", LoanStatus.Applied);

        var bureau = scope.ServiceProvider.GetRequiredService<ICreditBureau>();
        var listed = await bureau.CheckAsync(new BureauQuery("90123410", "Barasa Wekesa", "254700100010"), CancellationToken.None);
        listed.Status.ShouldBe(BureauStatus.Listed);
        listed.ListedAmount.ShouldBe(45_000m);

        var before = await db.Loans.AsNoTracking().FirstAsync(l => l.Id == applied.Id);
        var score = await scoring.ScoreLoanAsync(applied.Id, ScoreStage.Manual, DemoTenant.Users.LoanOfficer, CancellationToken.None);
        score.Stage.ShouldBe(ScoreStage.Manual);
        (await db.Loans.AsNoTracking().FirstAsync(l => l.Id == applied.Id)).Status.ShouldBe(before.Status, "scoring is advisory and never changes status");
    }

    [Fact]
    public async Task Recompute_needs_the_appraise_permission_and_decided_loans_cannot_be_rescored()
    {
        var applied = await SeededLoan("M00007", LoanStatus.Applied);
        var viewer = _factory.ClientAs(DemoTenant.Users.Teller, Permissions.Loans.View);
        (await viewer.PostAsync($"/api/loans/{applied.Id}/score", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var appraiser = _factory.ClientAs(DemoTenant.Users.LoanOfficer, Permissions.Loans.View, Permissions.Loans.Appraise);
        var fresh = await (await appraiser.PostAsync($"/api/loans/{applied.Id}/score", null)).ReadAs<CreditScoreResponse>();
        fresh.Stage.ShouldBe(ScoreStage.Manual);
        fresh.ComputedByUserId.ShouldBe(DemoTenant.Users.LoanOfficer);

        var active = await SeededLoan("M00001", LoanStatus.Active);
        var res = await appraiser.PostAsync($"/api/loans/{active.Id}/score", null);
        res.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await res.Content.ReadAsStringAsync()).ShouldContain("loans.scoring.decided");
    }

    [Fact]
    public async Task Scorecard_is_policy_behind_its_own_permission_and_validated()
    {
        var viewer = _factory.ClientAs(DemoTenant.Users.Accountant, Permissions.Loans.View);
        var card = await (await viewer.GetAsync("/api/loans/scoring/scorecard")).ReadAs<ScorecardResponse>();
        card.TotalPoints.ShouldBe(100);
        card.ApproveThreshold.ShouldBe(70);
        card.DeclineIfBureauListed.ShouldBeTrue();
        (await viewer.PutAsJsonAsync("/api/loans/scoring/scorecard", new SetScorecardRequest(card.Factors, 75, 55, true, "tightened"), HttpExtensions.JsonOptions)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var policyOwner = _factory.ClientAs(DemoTenant.Users.BranchManager, Permissions.Loans.View, Permissions.Loans.ScoringManage);
        var bad = await policyOwner.PutAsJsonAsync("/api/loans/scoring/scorecard", new SetScorecardRequest(card.Factors, 55, 75, true, "inverted"), HttpExtensions.JsonOptions);
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await bad.Content.ReadAsStringAsync()).ShouldContain("loans.scoring.threshold_order");

        var updated = await (await policyOwner.PutAsJsonAsync("/api/loans/scoring/scorecard", new SetScorecardRequest(card.Factors, 75, 55, true, "tightened"), HttpExtensions.JsonOptions)).ReadAs<ScorecardResponse>();
        updated.ApproveThreshold.ShouldBe(75);
        updated.Source.ShouldBe("tightened");
        try
        {
            (await (await viewer.GetAsync("/api/loans/scoring/scorecard")).ReadAs<ScorecardResponse>()).ApproveThreshold.ShouldBe(75);
        }
        finally
        {
            // Other tests score against the seeded policy; put it back.
            await policyOwner.PutAsJsonAsync("/api/loans/scoring/scorecard", new SetScorecardRequest(card.Factors, card.ApproveThreshold, card.ReferThreshold, card.DeclineIfBureauListed, card.Source), HttpExtensions.JsonOptions);
        }
    }
}
