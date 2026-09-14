using Sacco.Modules.Lending.Application;
using Sacco.Modules.Lending.Domain;
using Sacco.Shared.Domain;
using Shouldly;

namespace Sacco.UnitTests.Lending;

public sealed class CreditScoringEngineTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    private static Scorecard Card(int approve = 70, int refer = 50, bool declineIfListed = true)
        => Scorecard.Create(Guid.NewGuid(), Tenant, Scorecard.DefaultFactors, approve, refer, declineIfListed, "test", Guid.NewGuid(), Now);

    /// <summary>A textbook borrower: long tenure, saves monthly, clean history, well within limits, fully secured, bureau clear.</summary>
    private static ScoringInputs Strong(BureauStatus bureau = BureauStatus.Clear) => new(
        RequestedAmount: 100_000m, MaxEligibleAmount: 300_000m, BosaDeposits: 100_000m, ExistingOutstanding: 0m,
        MembershipMonths: 48, ContributionMonths: 46, PriorLoans: 2, WorstPriorArrearsDays: 0, CurrentArrearsAmount: 0m,
        AcceptedGuarantees: 100_000m, Bureau: bureau);

    [Fact]
    public void Strong_borrower_scores_high_and_is_recommended_for_approval()
    {
        var r = CreditScoringEngine.Score(Card(), Strong());
        r.Score.ShouldBe(100);
        r.Grade.ShouldBe("A");
        r.Recommendation.ShouldBe(ScoreRecommendation.Approve);
        r.Factors.Count.ShouldBe(Scorecard.DefaultFactors.Count);
        r.Factors.Sum(f => f.Points).ShouldBe(100m);
    }

    [Fact]
    public void Bureau_listing_overrides_the_points_when_policy_says_so()
    {
        var listed = CreditScoringEngine.Score(Card(), Strong(BureauStatus.Listed));
        listed.Score.ShouldBe(90); // only the 10-point bureau factor is lost
        listed.Recommendation.ShouldBe(ScoreRecommendation.Decline);
        listed.RecommendationReason.ShouldContain("bureau");

        var tolerated = CreditScoringEngine.Score(Card(declineIfListed: false), Strong(BureauStatus.Listed));
        tolerated.Recommendation.ShouldBe(ScoreRecommendation.Approve);
    }

    [Fact]
    public void Unavailable_bureau_turns_an_approve_into_a_referral()
    {
        var r = CreditScoringEngine.Score(Card(), Strong(BureauStatus.Unavailable));
        r.Score.ShouldBe(95);
        r.Recommendation.ShouldBe(ScoreRecommendation.Refer);
        r.Factors.Single(f => f.Key == ScoringFactors.CreditBureau).Points.ShouldBe(5m);
    }

    [Fact]
    public void Thin_file_is_neutral_not_penalised()
    {
        var first = Strong() with { PriorLoans = 0, WorstPriorArrearsDays = 0 };
        var r = CreditScoringEngine.Score(Card(), first);
        r.Factors.Single(f => f.Key == ScoringFactors.RepaymentHistory).Points.ShouldBe(15m); // 60% of 25
        r.Factors.Single(f => f.Key == ScoringFactors.RepaymentHistory).Value.ShouldBe("No prior loans");
    }

    [Fact]
    public void Serious_delinquency_and_current_arrears_push_below_the_refer_line()
    {
        var weak = new ScoringInputs(RequestedAmount: 280_000m, MaxEligibleAmount: 300_000m, BosaDeposits: 100_000m, ExistingOutstanding: 150_000m,
            MembershipMonths: 8, ContributionMonths: 3, PriorLoans: 1, WorstPriorArrearsDays: 400, CurrentArrearsAmount: 12_000m,
            AcceptedGuarantees: 0m, Bureau: BureauStatus.Clear);
        var r = CreditScoringEngine.Score(Card(), weak);
        r.Recommendation.ShouldBe(ScoreRecommendation.Decline);
        r.Grade.ShouldBeOneOf("D", "E");
        r.Factors.Single(f => f.Key == ScoringFactors.RepaymentHistory).Points.ShouldBe(0m);
        r.Factors.Single(f => f.Key == ScoringFactors.CurrentArrears).Points.ShouldBe(0m);
        r.Factors.Single(f => f.Key == ScoringFactors.ExistingExposure).Points.ShouldBe(0m); // 4.3× deposits
    }

    [Fact]
    public void Middle_band_is_referred_to_the_committee()
    {
        var middling = Strong() with { PriorLoans = 1, WorstPriorArrearsDays = 60, ContributionMonths = 20, MembershipMonths = 48, AcceptedGuarantees = 0m, RequestedAmount = 250_000m };
        var r = CreditScoringEngine.Score(Card(), middling);
        r.Score.ShouldBeInRange(50, 69);
        r.Recommendation.ShouldBe(ScoreRecommendation.Refer);
    }

    [Fact]
    public void Score_is_normalised_to_the_scorecards_total_points()
    {
        var heavy = Scorecard.Create(Guid.NewGuid(), Tenant, [new(ScoringFactors.CreditBureau, 40), new(ScoringFactors.CurrentArrears, 60)], 70, 50, true, null, Guid.NewGuid(), Now);
        heavy.TotalPoints.ShouldBe(100);
        CreditScoringEngine.Score(heavy, Strong(BureauStatus.Unavailable)).Score.ShouldBe(80); // 20 + 60
        var tiny = Scorecard.Create(Guid.NewGuid(), Tenant, [new(ScoringFactors.MembershipTenure, 3)], 70, 50, true, null, Guid.NewGuid(), Now);
        CreditScoringEngine.Score(tiny, Strong() with { MembershipMonths = 18 }).Score.ShouldBe(50);
    }

    [Theory]
    [InlineData(85, "A")] [InlineData(70, "B")] [InlineData(55, "C")] [InlineData(40, "D")] [InlineData(39, "E")]
    public void Grades_follow_fixed_bands(int score, string grade) => CreditScoringEngine.GradeFor(score).ShouldBe(grade);

    [Fact]
    public void Scorecard_rejects_unknown_duplicate_or_inverted_policy()
    {
        Should.Throw<DomainRuleException>(() => Scorecard.Create(Guid.NewGuid(), Tenant, [new("astrology", 10)], 70, 50, true, null, Guid.NewGuid(), Now)).Code.ShouldBe("loans.scoring.unknown_factor");
        Should.Throw<DomainRuleException>(() => Scorecard.Create(Guid.NewGuid(), Tenant, [new(ScoringFactors.CreditBureau, 10), new(ScoringFactors.CreditBureau, 5)], 70, 50, true, null, Guid.NewGuid(), Now)).Code.ShouldBe("loans.scoring.duplicate_factor");
        Should.Throw<DomainRuleException>(() => Scorecard.Create(Guid.NewGuid(), Tenant, Scorecard.DefaultFactors, 50, 70, true, null, Guid.NewGuid(), Now)).Code.ShouldBe("loans.scoring.threshold_order");
        Should.Throw<DomainRuleException>(() => Scorecard.Create(Guid.NewGuid(), Tenant, [new(ScoringFactors.CreditBureau, 0)], 70, 50, true, null, Guid.NewGuid(), Now)).Code.ShouldBe("loans.scoring.points_required");
    }
}
