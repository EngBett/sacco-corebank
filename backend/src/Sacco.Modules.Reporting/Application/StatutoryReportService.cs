using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sacco.Modules.Reporting.Domain;
using Sacco.Modules.Reporting.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Auth;
using Sacco.Shared.Notifications;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Lending;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Reporting.Application;

public sealed record ReportLine(string Code, string Name, decimal Amount);
public sealed record ReportSection(string Title, IReadOnlyList<ReportLine> Lines, decimal Total);
public sealed record FinancialPosition(Segment? Segment, DateOnly AsOf, ReportSection Assets, ReportSection Liabilities, ReportSection Equity, decimal AccumulatedSurplus, decimal InterSegmentNetted,
    decimal TotalAssets, decimal TotalLiabilitiesAndEquity, bool Balances);
public sealed record IncomeStatement(Segment? Segment, DateOnly From, DateOnly To, ReportSection Income, ReportSection Expenses, decimal Surplus);
public sealed record Ratio(string Name, decimal Numerator, decimal Denominator, decimal ValueBps, int MinimumBps, bool Compliant, string Basis);
public sealed record CapitalAdequacy(DateOnly AsOf, decimal ShareCapital, decimal InstitutionalCapital, decimal CoreCapital, decimal TotalAssets, decimal TotalDeposits, IReadOnlyList<Ratio> Ratios, string Source);
public sealed record LiquidityPosition(DateOnly AsOf, decimal LiquidAssets, decimal Deposits, decimal ShortTermLiabilities, Ratio Ratio, string Source);
public sealed record LargeExposure(Guid MemberId, int Loans, decimal Outstanding, decimal ShareOfCoreCapitalBps);
public sealed record LargeExposureReport(DateOnly AsOf, decimal CoreCapital, int ThresholdBps, IReadOnlyList<LargeExposure> Exposures, string Source);
public sealed record ReconciliationResult(bool IsReconciled, IReadOnlyList<string> Checks, IReadOnlyList<string> Failures);
public sealed record StatutoryReturnPackage(DateOnly PeriodStart, DateOnly PeriodEnd, DateTimeOffset GeneratedAt,
    FinancialPosition ConsolidatedPosition, FinancialPosition FosaPosition, FinancialPosition BosaPosition,
    IncomeStatement ConsolidatedIncome, IncomeStatement FosaIncome, IncomeStatement BosaIncome,
    CapitalAdequacy CapitalAdequacy, LiquidityPosition Liquidity, PortfolioQualitySnapshot PortfolioQuality, LargeExposureReport LargeExposures,
    decimal? SdgfContribution, ReconciliationResult Reconciliation, IReadOnlyList<string> OpenItems);

/// <summary>Builds every SASRA-facing statement from the ledger and lending contracts, and proves the package reconciles to the cent.</summary>
public sealed class StatutoryReportService(ReportingDbContext db, ILedgerService ledger, ILendingService lending, ITenantContext tenant, IClock clock, IAuditLogger audit, IOptions<ReportingSettings> options, INotifier notifier)
{
    private ReportingSettings S => options.Value;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private static decimal Signed(TrialBalanceLineSnapshot l, EntryDirection naturalSide) => l.NormalBalance == naturalSide ? l.Balance : -l.Balance;

    public async Task<FinancialPosition> FinancialPositionAsync(Segment? segment, DateOnly asOf, CancellationToken ct)
    {
        var tb = await ledger.GetTrialBalanceAsync(segment, asOf, ct);
        var consolidated = segment is null;
        var clearing = tb.Lines.Where(l => S.InterSegmentClearingGls.Contains(l.Code)).ToList();
        var lines = consolidated ? tb.Lines.Where(l => !S.InterSegmentClearingGls.Contains(l.Code)).ToList() : tb.Lines.ToList();

        ReportSection Section(string title, string category, EntryDirection natural)
        {
            var rows = lines.Where(l => l.Category == category).Select(l => new ReportLine(l.Code, l.Name, Signed(l, natural))).ToList();
            return new ReportSection(title, rows, rows.Sum(r => r.Amount));
        }
        var assets = Section("Assets", "Asset", EntryDirection.Debit);
        var liabilities = Section("Liabilities", "Liability", EntryDirection.Credit);
        var equity = Section("Equity", "Equity", EntryDirection.Credit);
        var surplus = lines.Where(l => l.Category == "Income").Sum(l => Signed(l, EntryDirection.Credit)) - lines.Where(l => l.Category == "Expense").Sum(l => Signed(l, EntryDirection.Debit));
        var netted = consolidated ? clearing.Where(l => l.Category == "Asset").Sum(l => Signed(l, EntryDirection.Debit)) : 0m;
        var totalLe = liabilities.Total + equity.Total + surplus;
        return new FinancialPosition(segment, asOf, assets, liabilities, equity, surplus, netted, assets.Total, totalLe, assets.Total == totalLe);
    }

    public async Task<IncomeStatement> IncomeStatementAsync(Segment? segment, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var activity = await ledger.GetGlActivityAsync(from, to, segment, ct);
        var income = activity.Where(a => a.Category == "Income").Select(a => new ReportLine(a.Code, a.Name, a.Credit - a.Debit)).ToList();
        var expenses = activity.Where(a => a.Category == "Expense").Select(a => new ReportLine(a.Code, a.Name, a.Debit - a.Credit)).ToList();
        var incomeSection = new ReportSection("Income", income, income.Sum(l => l.Amount));
        var expenseSection = new ReportSection("Expenses", expenses, expenses.Sum(l => l.Amount));
        return new IncomeStatement(segment, from, to, incomeSection, expenseSection, incomeSection.Total - expenseSection.Total);
    }

    public async Task<CapitalAdequacy> CapitalAdequacyAsync(DateOnly asOf, CancellationToken ct)
    {
        var position = await FinancialPositionAsync(null, asOf, ct);
        var tb = await ledger.GetTrialBalanceAsync(null, asOf, ct);
        decimal Sum(IEnumerable<string> codes, EntryDirection natural) => tb.Lines.Where(l => codes.Contains(l.Code)).Sum(l => Signed(l, natural));
        var shareCapital = Sum(S.ShareCapitalGls, EntryDirection.Credit);
        var institutional = Sum(S.InstitutionalCapitalGls, EntryDirection.Credit) + position.AccumulatedSurplus;
        var core = shareCapital + institutional;
        var deposits = Sum(S.MemberDepositGls, EntryDirection.Credit);
        var assets = position.TotalAssets;
        Ratio R(string name, decimal num, decimal den, int min, string basis) { var bps = den == 0 ? 0 : decimal.Round(num / den * 10_000m, 0); return new Ratio(name, num, den, bps, min, bps >= min, basis); }
        return new CapitalAdequacy(asOf, shareCapital, institutional, core, assets, deposits,
        [
            R("Core capital / total assets", core, assets, S.CoreCapitalToAssetsMinBps, "Core capital = share capital + institutional capital (reserves, retained earnings, current surplus)"),
            R("Institutional capital / total assets", institutional, assets, S.InstitutionalCapitalToAssetsMinBps, "Institutional capital = reserves + retained earnings + current surplus"),
            R("Core capital / total deposits", core, deposits, S.CoreCapitalToDepositsMinBps, "Deposits = member deposits (BOSA + FOSA) + fixed deposits"),
        ], S.ThresholdSource);
    }

    public async Task<LiquidityPosition> LiquidityAsync(DateOnly asOf, CancellationToken ct)
    {
        var tb = await ledger.GetTrialBalanceAsync(null, asOf, ct);
        decimal Sum(IEnumerable<string> codes, EntryDirection natural) => tb.Lines.Where(l => codes.Contains(l.Code)).Sum(l => Signed(l, natural));
        var liquid = Sum(S.LiquidAssetGls, EntryDirection.Debit);
        var deposits = Sum(S.MemberDepositGls, EntryDirection.Credit);
        var shortTerm = Sum(S.ShortTermLiabilityGls, EntryDirection.Credit);
        var den = deposits + shortTerm;
        var bps = den == 0 ? 0 : decimal.Round(liquid / den * 10_000m, 0);
        return new LiquidityPosition(asOf, liquid, deposits, shortTerm, new Ratio("Liquid assets / (deposits + short-term liabilities)", liquid, den, bps, S.LiquidityMinBps, bps >= S.LiquidityMinBps, "Liquid assets = cash, bank and mobile-money settlement balances; FOSA demand liabilities weighting to be confirmed"), S.ThresholdSource);
    }

    public async Task<LargeExposureReport> LargeExposuresAsync(DateOnly asOf, CancellationToken ct)
    {
        var capital = await CapitalAdequacyAsync(asOf, ct);
        var portfolio = await lending.GetPortfolioQualityAsync(asOf, ct);
        var exposures = portfolio.ByMember
            .Select(m => new LargeExposure(m.MemberId, m.Loans, m.Outstanding, capital.CoreCapital == 0 ? 0 : decimal.Round(m.Outstanding / capital.CoreCapital * 10_000m, 0)))
            .Where(e => e.ShareOfCoreCapitalBps >= S.LargeExposureThresholdBps)
            .OrderByDescending(e => e.Outstanding).ToList();
        return new LargeExposureReport(asOf, capital.CoreCapital, S.LargeExposureThresholdBps, exposures, S.ThresholdSource);
    }

    public async Task<StatutoryReturnPackage> BuildPackageAsync(DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        var consolidated = await FinancialPositionAsync(null, periodEnd, ct);
        var fosa = await FinancialPositionAsync(Segment.Fosa, periodEnd, ct);
        var bosa = await FinancialPositionAsync(Segment.Bosa, periodEnd, ct);
        var incomeAll = await IncomeStatementAsync(null, periodStart, periodEnd, ct);
        var incomeFosa = await IncomeStatementAsync(Segment.Fosa, periodStart, periodEnd, ct);
        var incomeBosa = await IncomeStatementAsync(Segment.Bosa, periodStart, periodEnd, ct);
        var capital = await CapitalAdequacyAsync(periodEnd, ct);
        var liquidity = await LiquidityAsync(periodEnd, ct);
        var portfolio = await lending.GetPortfolioQualityAsync(periodEnd, ct);
        var exposures = await LargeExposuresAsync(periodEnd, ct);
        var tb = await ledger.GetTrialBalanceAsync(null, periodEnd, ct);
        var recon = await ledger.ReconcileAsync(ct);

        var checks = new List<string>(); var failures = new List<string>();
        void Check(string name, bool ok, string detail) { if (ok) checks.Add($"{name}: OK ({detail})"); else failures.Add($"{name}: FAILED ({detail})"); }
        Check("Trial balance balances", tb.IsBalanced, $"debits {tb.TotalDebits:N2} = credits {tb.TotalCredits:N2}");
        Check("Consolidated position balances", consolidated.Balances, $"assets {consolidated.TotalAssets:N2} = liabilities + equity + surplus {consolidated.TotalLiabilitiesAndEquity:N2}");
        Check("FOSA position balances", fosa.Balances, $"assets {fosa.TotalAssets:N2} vs {fosa.TotalLiabilitiesAndEquity:N2}");
        Check("BOSA position balances", bosa.Balances, $"assets {bosa.TotalAssets:N2} vs {bosa.TotalLiabilitiesAndEquity:N2}");
        Check("Segments sum to consolidated (assets)", fosa.TotalAssets + bosa.TotalAssets - consolidated.InterSegmentNetted == consolidated.TotalAssets, $"{fosa.TotalAssets:N2} + {bosa.TotalAssets:N2} − clearing {consolidated.InterSegmentNetted:N2} = {consolidated.TotalAssets:N2}");
        Check("Segment income statements sum to consolidated", incomeFosa.Surplus + incomeBosa.Surplus == incomeAll.Surplus, $"{incomeFosa.Surplus:N2} + {incomeBosa.Surplus:N2} = {incomeAll.Surplus:N2}");
        Check("Running balances reconcile to journal lines and sub-ledgers", recon.IsClean, recon.IsClean ? $"{recon.GlAccountsChecked} GL / {recon.ControlAccountsChecked} control accounts" : string.Join("; ", recon.Issues));
        var loanControl = tb.Lines.Where(l => S.LoanControlGls.Contains(l.Code)).Sum(l => Signed(l, EntryDirection.Debit));
        Check("Loan portfolio agrees with loan control accounts", loanControl == portfolio.TotalOutstanding, $"portfolio {portfolio.TotalOutstanding:N2} vs GL {loanControl:N2}");

        var sdgf = S.SdgfContributionBps is int bps ? decimal.Round(capital.TotalDeposits * bps / 10_000m, 2) : (decimal?)null;
        var openItems = new List<string>
        {
            "Confirm current SASRA electronic submission format/mechanism before first live submission.",
            "Confirm current provisioning percentages and aging bucket definitions (per SASRA circular): " + (portfolio.ConfigSource ?? "not configured"),
            "Confirm SDGF contribution basis; Reporting:SdgfContributionBps is " + (S.SdgfContributionBps?.ToString() ?? "not set"),
            "Confirm liquidity weighting of FOSA demand liabilities.",
        };
        return new StatutoryReturnPackage(periodStart, periodEnd, clock.UtcNow, consolidated, fosa, bosa, incomeAll, incomeFosa, incomeBosa, capital, liquidity, portfolio, exposures, sdgf,
            new ReconciliationResult(failures.Count == 0, checks, failures), openItems);
    }

    public async Task<StatutoryReturn> GenerateAsync(DateOnly periodStart, DateOnly periodEnd, Guid byUser, CancellationToken ct)
    {
        if (await db.Returns.AnyAsync(r => r.PeriodEnd == periodEnd && r.Status != ReturnStatus.Withdrawn, ct))
            throw new ConflictException("reporting.return_exists", $"A return for the period ending {periodEnd:yyyy-MM-dd} already exists; withdraw it first to regenerate.");
        var package = await BuildPackageAsync(periodStart, periodEnd, ct);
        var notes = package.Reconciliation.IsReconciled ? string.Join(" | ", package.Reconciliation.Checks) : string.Join(" | ", package.Reconciliation.Failures);
        var ret = StatutoryReturn.Create(Ids.New(), tenant.TenantId, periodStart, periodEnd, byUser, clock.UtcNow, package.Reconciliation.IsReconciled, notes, JsonSerializer.Serialize(package, Json));
        db.Returns.Add(ret);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("reporting.return.generated", nameof(StatutoryReturn), ret.Id.ToString(), byUser, $$"""{"periodEnd":"{{periodEnd:yyyy-MM-dd}}","reconciled":{{package.Reconciliation.IsReconciled.ToString().ToLower()}}}"""), ct);
        await notifier.NotifyAsync(new NotificationRequest("reporting.return.generated", $"Statutory return to {periodEnd:d MMM yyyy} is ready for submission",
            package.Reconciliation.IsReconciled ? "All reconciliation checks passed" : "Reconciliation checks failed; it cannot be submitted yet", $"/reporting/{ret.Id}", NotificationAudience.HoldersOf(Permissions.Reporting.StatutorySubmit), byUser), ct);
        return ret;
    }

    public async Task<StatutoryReturn> GetAsync(Guid id, CancellationToken ct) => await db.Returns.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Statutory return", id);

    public async Task<StatutoryReturn> SubmitAsync(Guid id, string submissionReference, Guid byUser, CancellationToken ct)
    {
        var ret = await GetAsync(id, ct);
        ret.Submit(byUser, submissionReference, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("reporting.return.submitted", nameof(StatutoryReturn), ret.Id.ToString(), byUser, $$"""{"periodEnd":"{{ret.PeriodEnd:yyyy-MM-dd}}","reference":"{{submissionReference}}","generatedBy":"{{ret.GeneratedByUserId}}"}"""), ct);
        await notifier.NotifyAsync(new NotificationRequest("reporting.return.submitted", $"Statutory return to {ret.PeriodEnd:d MMM yyyy} submitted to SASRA",
            $"Submission reference {submissionReference}", $"/reporting/{ret.Id}", NotificationAudience.User(ret.GeneratedByUserId), byUser), ct);
        return ret;
    }

    public async Task<StatutoryReturn> WithdrawAsync(Guid id, string reason, Guid byUser, CancellationToken ct)
    {
        var ret = await GetAsync(id, ct);
        ret.Withdraw(byUser, reason);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("reporting.return.withdrawn", nameof(StatutoryReturn), ret.Id.ToString(), byUser, $$"""{"reason":"{{reason.Replace("\"", "'")}}"}"""), ct);
        return ret;
    }

    public static StatutoryReturnPackage Deserialize(string json) => JsonSerializer.Deserialize<StatutoryReturnPackage>(json, Json)!;
}
