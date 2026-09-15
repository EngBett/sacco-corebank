using System.Globalization;
using System.Text;
using Sacco.Shared.Ledger;

namespace Sacco.Modules.Reporting.Application;

/// <summary>
/// Flat, section-tagged CSV of a statutory package: one row per figure, so it can be pasted into whatever
/// template SASRA's portal expects once the format is confirmed (confirmation register item 7).
/// </summary>
public static class StatutoryReturnCsv
{
    public static string Render(StatutoryReturnPackage p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("section,segment,code,name,amount,note");
        void Row(string section, string segment, string code, string name, decimal amount, string note = "")
            => sb.AppendLine(string.Join(',', Csv(section), Csv(segment), Csv(code), Csv(name), amount.ToString("0.00", CultureInfo.InvariantCulture), Csv(note)));
        void Position(FinancialPosition fp)
        {
            var seg = fp.Segment?.ToString() ?? "Consolidated";
            foreach (var s in new[] { fp.Assets, fp.Liabilities, fp.Equity })
                foreach (var l in s.Lines) Row("FinancialPosition", seg, l.Code, l.Name, l.Amount, s.Title);
            Row("FinancialPosition", seg, "", "Accumulated surplus", fp.AccumulatedSurplus);
            Row("FinancialPosition", seg, "", "Total assets", fp.TotalAssets);
            Row("FinancialPosition", seg, "", "Total liabilities and equity", fp.TotalLiabilitiesAndEquity, fp.Balances ? "balances" : "DOES NOT BALANCE");
        }
        void Income(IncomeStatement st)
        {
            var seg = st.Segment?.ToString() ?? "Consolidated";
            foreach (var l in st.Income.Lines) Row("IncomeStatement", seg, l.Code, l.Name, l.Amount, "Income");
            foreach (var l in st.Expenses.Lines) Row("IncomeStatement", seg, l.Code, l.Name, l.Amount, "Expense");
            Row("IncomeStatement", seg, "", "Surplus", st.Surplus);
        }
        Row("Period", "", "", "Period start", 0, p.PeriodStart.ToString("yyyy-MM-dd"));
        Row("Period", "", "", "Period end", 0, p.PeriodEnd.ToString("yyyy-MM-dd"));
        Position(p.ConsolidatedPosition); Position(p.FosaPosition); Position(p.BosaPosition);
        Income(p.ConsolidatedIncome); Income(p.FosaIncome); Income(p.BosaIncome);
        foreach (var r in p.CapitalAdequacy.Ratios) Row("CapitalAdequacy", "", "", r.Name, r.ValueBps / 100m, $"minimum {r.MinimumBps / 100m}% {(r.Compliant ? "compliant" : "BREACH")}; {r.Basis}");
        Row("CapitalAdequacy", "", "", "Core capital", p.CapitalAdequacy.CoreCapital);
        Row("CapitalAdequacy", "", "", "Institutional capital", p.CapitalAdequacy.InstitutionalCapital);
        Row("Liquidity", "", "", "Liquid assets", p.Liquidity.LiquidAssets);
        Row("Liquidity", "", "", "Deposits", p.Liquidity.Deposits);
        Row("Liquidity", "", "", "Liquidity ratio", p.Liquidity.Ratio.ValueBps / 100m, $"minimum {p.Liquidity.Ratio.MinimumBps / 100m}% {(p.Liquidity.Ratio.Compliant ? "compliant" : "BREACH")}");
        foreach (var b in p.PortfolioQuality.Buckets) Row("PortfolioQuality", "", "", b.Name, b.Outstanding, $"{b.Loans} loans; provision {b.ProvisionRequired.ToString("0.00", CultureInfo.InvariantCulture)} at {b.ProvisionRateBps / 100m}%");
        Row("PortfolioQuality", "", "", "Non-performing outstanding", p.PortfolioQuality.NonPerformingOutstanding);
        Row("PortfolioQuality", "", "", "Provision required", p.PortfolioQuality.ProvisionRequired);
        foreach (var e in p.LargeExposures.Exposures) Row("LargeExposures", "", e.MemberId.ToString(), "Member exposure", e.Outstanding, $"{e.ShareOfCoreCapitalBps / 100m}% of core capital");
        if (p.SdgfContribution is { } sdgf) Row("SDGF", "", "", "Deposit guarantee fund contribution", sdgf);
        foreach (var c in p.Reconciliation.Checks) Row("Reconciliation", "", "", c, 0, p.Reconciliation.Failures.Contains(c) ? "FAILED" : "passed");
        foreach (var o in p.OpenItems) Row("OpenItems", "", "", o, 0, "awaiting SASRA confirmation");
        return sb.ToString();
    }

    private static string Csv(string s) => s.Contains(',') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
