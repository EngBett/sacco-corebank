using Sacco.Modules.Lending.Domain;

namespace Sacco.Modules.Lending.Application;

/// <summary>Everything the engine looks at, gathered by the service so the engine itself is a pure, unit-testable function.</summary>
public sealed record ScoringInputs(
    decimal RequestedAmount,
    decimal MaxEligibleAmount,
    decimal BosaDeposits,
    decimal ExistingOutstanding,
    int MembershipMonths,
    int ContributionMonths,
    /// <summary>Number of earlier loans (any status other than Rejected) the member has had with the SACCO.</summary>
    int PriorLoans,
    /// <summary>Worst days-in-arrears observed on earlier loans (current or historic). 0 when none.</summary>
    int WorstPriorArrearsDays,
    /// <summary>Arrears amount outstanding today on active loans.</summary>
    decimal CurrentArrearsAmount,
    decimal AcceptedGuarantees,
    BureauStatus Bureau);

/// <summary>
/// Turns inputs into points per factor, a 0–100 score, a grade and a recommendation. Pure and
/// deterministic: the same inputs and scorecard always produce the same result, which is what makes
/// a stored score auditable.
/// </summary>
public static class CreditScoringEngine
{
    public static ScoreResult Score(Scorecard card, ScoringInputs x)
    {
        var factors = card.Factors.Select(f => Evaluate(f, x)).ToList();
        var total = card.TotalPoints;
        var earned = factors.Sum(f => f.Points);
        var score = total == 0 ? 0 : (int)Math.Round(earned / total * 100m, MidpointRounding.AwayFromZero);
        var grade = GradeFor(score);

        ScoreRecommendation recommendation;
        string reason;
        if (card.DeclineIfBureauListed && x.Bureau == BureauStatus.Listed)
        {
            recommendation = ScoreRecommendation.Decline;
            reason = "Adverse credit bureau listing (policy: decline when listed).";
        }
        else if (score >= card.ApproveThreshold)
        {
            recommendation = ScoreRecommendation.Approve;
            reason = $"Score {score} is at or above the approve threshold of {card.ApproveThreshold}.";
        }
        else if (score >= card.ReferThreshold)
        {
            recommendation = ScoreRecommendation.Refer;
            reason = $"Score {score} is between the refer threshold of {card.ReferThreshold} and the approve threshold of {card.ApproveThreshold}; committee judgement required.";
        }
        else
        {
            recommendation = ScoreRecommendation.Decline;
            reason = $"Score {score} is below the refer threshold of {card.ReferThreshold}.";
        }
        if (x.Bureau == BureauStatus.Unavailable && recommendation == ScoreRecommendation.Approve)
        {
            recommendation = ScoreRecommendation.Refer;
            reason = "Bureau result unavailable; an approve-band score is referred for committee judgement.";
        }
        return new ScoreResult(score, grade, recommendation, reason, factors);
    }

    public static string GradeFor(int score) => score switch { >= 85 => "A", >= 70 => "B", >= 55 => "C", >= 40 => "D", _ => "E" };

    private static FactorScore Evaluate(ScorecardFactor f, ScoringInputs x)
    {
        var (share, value, note) = f.Key switch
        {
            ScoringFactors.RepaymentHistory => RepaymentHistory(x),
            ScoringFactors.CurrentArrears => x.CurrentArrearsAmount > 0
                ? (0m, $"KES {x.CurrentArrearsAmount:N2} in arrears", "An active loan is in arrears today.")
                : (1m, "None", "No active loan is in arrears."),
            ScoringFactors.SavingsConsistency => SavingsConsistency(x),
            ScoringFactors.MembershipTenure => (Math.Min(1m, x.MembershipMonths / 36m), $"{x.MembershipMonths} months", "Full points at 36 months."),
            ScoringFactors.DepositCoverage => DepositCoverage(x),
            ScoringFactors.ExistingExposure => ExistingExposure(x),
            ScoringFactors.GuarantorCoverage => GuarantorCoverage(x),
            ScoringFactors.CreditBureau => x.Bureau switch
            {
                BureauStatus.Clear => (1m, "Clear", "No adverse listings."),
                BureauStatus.Listed => (0m, "Listed", "Adverse listing on file."),
                _ => (0.5m, "Unavailable", "Bureau could not be reached; half points."),
            },
            _ => (0m, "n/a", "Unknown factor."),
        };
        var points = decimal.Round(share * f.MaxPoints, 1, MidpointRounding.AwayFromZero);
        return new FactorScore(f.Key, ScoringFactors.LabelOf(f.Key), value, points, f.MaxPoints, note);
    }

    private static (decimal, string, string) RepaymentHistory(ScoringInputs x)
    {
        if (x.PriorLoans == 0) return (0.6m, "No prior loans", "Thin file: scored neutrally.");
        var d = x.WorstPriorArrearsDays;
        return d switch
        {
            0 => (1m, $"{x.PriorLoans} prior loan(s), never in arrears", "Clean history."),
            <= 30 => (0.7m, $"Worst arrears {d} days", "Minor delays only."),
            <= 90 => (0.4m, $"Worst arrears {d} days", "Repeated or prolonged delays."),
            <= 180 => (0.15m, $"Worst arrears {d} days", "Serious delinquency on a prior loan."),
            _ => (0m, $"Worst arrears {d} days", "Prior loan classified doubtful or loss."),
        };
    }

    private static (decimal, string, string) SavingsConsistency(ScoringInputs x)
    {
        var months = Math.Max(1, x.MembershipMonths);
        var ratio = Math.Min(1m, x.ContributionMonths / (decimal)months);
        var share = ratio >= 0.9m ? 1m : decimal.Round(ratio / 0.9m, 3);
        return (share, $"{x.ContributionMonths} of {months} months", ratio >= 0.9m ? "Contributes almost every month." : "Gaps in monthly contributions.");
    }

    private static (decimal, string, string) DepositCoverage(ScoringInputs x)
    {
        if (x.MaxEligibleAmount <= 0) return (0m, "No eligible amount", "Deposits do not support this product.");
        var ratio = x.RequestedAmount / x.MaxEligibleAmount;
        var share = ratio switch { <= 0.5m => 1m, <= 0.75m => 0.7m, <= 1m => 0.4m, _ => 0m };
        return (share, $"{ratio:P0} of maximum eligible", share == 1m ? "Comfortably within the deposit multiplier." : "Close to the deposit multiplier ceiling.");
    }

    private static (decimal, string, string) ExistingExposure(ScoringInputs x)
    {
        if (x.BosaDeposits <= 0) return (0m, "No BOSA deposits", "No deposits to set exposure against.");
        var ratio = (x.ExistingOutstanding + x.RequestedAmount) / x.BosaDeposits;
        var share = ratio switch { <= 1m => 1m, <= 2m => 0.6m, <= 3m => 0.3m, _ => 0m };
        return (share, $"{ratio:0.0}× deposits after this loan", share == 1m ? "Exposure fully covered by deposits." : "Exposure exceeds deposits.");
    }

    private static (decimal, string, string) GuarantorCoverage(ScoringInputs x)
    {
        if (x.RequestedAmount <= 0) return (0m, "n/a", "No amount.");
        var security = x.AcceptedGuarantees + Math.Min(x.BosaDeposits, x.RequestedAmount);
        var ratio = Math.Min(1m, security / x.RequestedAmount);
        return (decimal.Round(ratio, 3), $"{ratio:P0} secured", ratio >= 1m ? "Fully secured by guarantees and own deposits." : "Not yet fully secured; guarantees may still be pending.");
    }
}
