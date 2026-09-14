using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Lending.Domain;

public enum ScoreRecommendation { Approve = 1, Refer = 2, Decline = 3 }
public enum ScoreStage { Application = 1, Appraisal = 2, Manual = 3 }
public enum BureauStatus { Clear = 1, Listed = 2, Unavailable = 3 }

/// <summary>
/// The fixed catalogue of things a scorecard can weigh. Keys are stable identifiers stored on the
/// scorecard; the engine knows how to turn each one into points. Adding a factor means adding it
/// here and in <c>CreditScoringEngine</c>, never in configuration alone.
/// </summary>
public static class ScoringFactors
{
    public const string RepaymentHistory = "repayment_history";
    public const string CurrentArrears = "current_arrears";
    public const string SavingsConsistency = "savings_consistency";
    public const string MembershipTenure = "membership_tenure";
    public const string DepositCoverage = "deposit_coverage";
    public const string ExistingExposure = "existing_exposure";
    public const string GuarantorCoverage = "guarantor_coverage";
    public const string CreditBureau = "credit_bureau";

    public static readonly IReadOnlyList<ScoringFactorDefinition> All =
    [
        new(RepaymentHistory, "Repayment history", "Worst arrears on the member's previous loans with this SACCO. A member with no prior loan is scored neutrally (thin file)."),
        new(CurrentArrears, "Current arrears", "Whether any active loan is in arrears today."),
        new(SavingsConsistency, "Savings consistency", "Months with a BOSA contribution as a share of months of membership."),
        new(MembershipTenure, "Membership tenure", "Months since joining; full points at three years."),
        new(DepositCoverage, "Deposit coverage", "Requested amount as a share of the maximum the member's deposits allow."),
        new(ExistingExposure, "Existing exposure", "Outstanding loans plus this request, relative to BOSA deposits."),
        new(GuarantorCoverage, "Guarantor coverage", "Accepted guarantees plus own deposits, relative to the amount. Refreshed at appraisal."),
        new(CreditBureau, "Credit bureau", "Credit reference bureau check: clear, listed, or unavailable."),
    ];

    public static bool IsKnown(string key) => All.Any(f => f.Key == key);
    public static string LabelOf(string key) => All.FirstOrDefault(f => f.Key == key)?.Label ?? key;
}

public sealed record ScoringFactorDefinition(string Key, string Label, string Description);
public sealed record ScorecardFactorDraft(string Key, int MaxPoints);

/// <summary>
/// Per-tenant credit policy: how much each factor weighs and where the approve/refer/decline cut-offs
/// sit. The score advises the appraiser and the committee; it never approves anything (non-negotiable #6).
/// </summary>
public class Scorecard : TenantEntity
{
    private readonly List<ScorecardFactor> _factors = [];
    private Scorecard() { }

    public IReadOnlyList<ScorecardFactor> Factors => _factors;
    /// <summary>Score (0–100) at or above which the engine recommends Approve.</summary>
    public int ApproveThreshold { get; private set; }
    /// <summary>Score (0–100) at or above which the engine recommends Refer; below it, Decline.</summary>
    public int ReferThreshold { get; private set; }
    /// <summary>A bureau listing recommends Decline regardless of the points score.</summary>
    public bool DeclineIfBureauListed { get; private set; }
    public string? Source { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid UpdatedByUserId { get; private set; }

    public int TotalPoints => _factors.Sum(f => f.MaxPoints);

    public static Scorecard Create(Guid id, Guid tenantId, IEnumerable<ScorecardFactorDraft> factors, int approveThreshold, int referThreshold, bool declineIfBureauListed, string? source, Guid by, DateTimeOffset now)
    {
        var card = new Scorecard { Id = id, TenantId = tenantId };
        card.Replace(factors, approveThreshold, referThreshold, declineIfBureauListed, source, by, now);
        return card;
    }

    /// <summary>The policy every tenant starts from; the seed tool and the lazy fallback both use it.</summary>
    public static IReadOnlyList<ScorecardFactorDraft> DefaultFactors =>
    [
        new(ScoringFactors.RepaymentHistory, 25),
        new(ScoringFactors.CurrentArrears, 10),
        new(ScoringFactors.SavingsConsistency, 15),
        new(ScoringFactors.MembershipTenure, 10),
        new(ScoringFactors.DepositCoverage, 10),
        new(ScoringFactors.ExistingExposure, 10),
        new(ScoringFactors.GuarantorCoverage, 10),
        new(ScoringFactors.CreditBureau, 10),
    ];

    /// <summary>Replaces the policy. Returns the new factor rows so a service can register them as Added on a tracked card.</summary>
    public IReadOnlyList<ScorecardFactor> Replace(IEnumerable<ScorecardFactorDraft> drafts, int approveThreshold, int referThreshold, bool declineIfBureauListed, string? source, Guid by, DateTimeOffset now)
    {
        var list = drafts.ToList();
        if (list.Count == 0) throw new DomainRuleException("loans.scoring.factors_required", "A scorecard needs at least one factor.");
        var unknown = list.Where(d => !ScoringFactors.IsKnown(d.Key)).Select(d => d.Key).ToList();
        if (unknown.Count > 0) throw new DomainRuleException("loans.scoring.unknown_factor", $"Unknown scoring factor(s): {string.Join(", ", unknown)}.");
        if (list.Select(d => d.Key).Distinct().Count() != list.Count) throw new DomainRuleException("loans.scoring.duplicate_factor", "Each factor may appear once.");
        if (list.Any(d => d.MaxPoints < 0 || d.MaxPoints > 100)) throw new DomainRuleException("loans.scoring.points_range", "Factor points must be between 0 and 100.");
        if (list.Sum(d => d.MaxPoints) <= 0) throw new DomainRuleException("loans.scoring.points_required", "At least one factor must carry points.");
        if (approveThreshold is < 0 or > 100 || referThreshold is < 0 or > 100) throw new DomainRuleException("loans.scoring.threshold_range", "Thresholds are scores between 0 and 100.");
        if (referThreshold >= approveThreshold) throw new DomainRuleException("loans.scoring.threshold_order", "The refer threshold must be below the approve threshold.");

        _factors.Clear();
        var added = list.Select(d => ScorecardFactor.Create(Id, d.Key, d.MaxPoints)).ToList();
        _factors.AddRange(added);
        ApproveThreshold = approveThreshold; ReferThreshold = referThreshold; DeclineIfBureauListed = declineIfBureauListed;
        Source = source; UpdatedByUserId = by; UpdatedAt = now;
        return added;
    }
}

public class ScorecardFactor
{
    private ScorecardFactor() { }
    public Guid Id { get; private set; }
    public Guid ScorecardId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public int MaxPoints { get; private set; }
    internal static ScorecardFactor Create(Guid scorecardId, string key, int maxPoints) => new() { Id = Ids.New(), ScorecardId = scorecardId, Key = key, MaxPoints = maxPoints };
}

/// <summary>One factor's contribution, as computed. Stored as JSON on the score so the committee sees exactly what the engine saw.</summary>
public sealed record FactorScore(string Key, string Label, string Value, decimal Points, int MaxPoints, string Note);

/// <summary>Output of the engine before it is persisted.</summary>
public sealed record ScoreResult(int Score, string Grade, ScoreRecommendation Recommendation, string RecommendationReason, IReadOnlyList<FactorScore> Factors);

/// <summary>
/// A computed score for a loan at a point in time. Every computation is kept (application, appraisal,
/// manual refresh) so the record shows what the committee had in front of it when it decided.
/// </summary>
public class LoanCreditScore : TenantEntity
{
    private LoanCreditScore() { }

    public Guid LoanId { get; private set; }
    public ScoreStage Stage { get; private set; }
    public int Score { get; private set; }
    public string Grade { get; private set; } = string.Empty;
    public ScoreRecommendation Recommendation { get; private set; }
    public string RecommendationReason { get; private set; } = string.Empty;
    public BureauStatus BureauStatus { get; private set; }
    public string? BureauReference { get; private set; }
    public string? BureauNarrative { get; private set; }
    /// <summary>JSON array of <see cref="FactorScore"/>.</summary>
    public string FactorsJson { get; private set; } = "[]";
    public DateTimeOffset ScorecardUpdatedAt { get; private set; }
    public DateTimeOffset ComputedAt { get; private set; }
    public Guid ComputedByUserId { get; private set; }

    public static LoanCreditScore Create(Guid tenantId, Guid loanId, ScoreStage stage, ScoreResult result, BureauStatus bureauStatus, string? bureauReference, string? bureauNarrative, string factorsJson, DateTimeOffset scorecardUpdatedAt, Guid by, DateTimeOffset now)
        => new()
        {
            Id = Ids.New(), TenantId = tenantId, LoanId = loanId, Stage = stage,
            Score = result.Score, Grade = result.Grade, Recommendation = result.Recommendation, RecommendationReason = result.RecommendationReason,
            BureauStatus = bureauStatus, BureauReference = bureauReference, BureauNarrative = bureauNarrative,
            FactorsJson = factorsJson, ScorecardUpdatedAt = scorecardUpdatedAt, ComputedByUserId = by, ComputedAt = now,
        };
}
