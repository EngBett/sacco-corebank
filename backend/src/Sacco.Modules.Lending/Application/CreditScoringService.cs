using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sacco.Modules.Lending.Domain;
using Sacco.Modules.Lending.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Lending;
using Sacco.Shared.Members;
using Sacco.Shared.Savings;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Lending.Application;

/// <summary>
/// Gathers the inputs (member standing, savings, prior loans, guarantees, bureau), runs the engine
/// against the tenant's scorecard and stores the result. Runs automatically at application and at
/// appraisal; an appraiser can also refresh it. It advises — approval stays with people.
/// </summary>
public sealed class CreditScoringService(LendingDbContext db, IMemberDirectory members, ISavingsService savings, ILedgerService ledger, ICreditBureau bureau, ITenantContext tenant, IClock clock, IAuditLogger audit, ILogger<CreditScoringService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Scorecard> GetScorecardAsync(CancellationToken ct)
        => await db.Scorecards.FirstOrDefaultAsync(ct) ?? await CreateDefaultScorecardAsync(ct);

    private async Task<Scorecard> CreateDefaultScorecardAsync(CancellationToken ct)
    {
        var card = Scorecard.Create(Ids.New(), tenant.TenantId, Scorecard.DefaultFactors, 70, 50, true, "Platform default scorecard — review against the SACCO's credit policy", SystemActors.System, clock.UtcNow);
        db.Scorecards.Add(card);
        await db.SaveChangesAsync(ct);
        return card;
    }

    public async Task<Scorecard> SetScorecardAsync(IReadOnlyList<ScorecardFactorDraft> factors, int approveThreshold, int referThreshold, bool declineIfBureauListed, string? source, Guid byUser, CancellationToken ct)
    {
        var card = await db.Scorecards.FirstOrDefaultAsync(ct);
        if (card is null)
        {
            card = Scorecard.Create(Ids.New(), tenant.TenantId, factors, approveThreshold, referThreshold, declineIfBureauListed, source, byUser, clock.UtcNow);
            db.Scorecards.Add(card);
        }
        else
        {
            db.RemoveRange(card.Factors);
            var added = card.Replace(factors, approveThreshold, referThreshold, declineIfBureauListed, source, byUser, clock.UtcNow);
            db.AddRange(added);
        }
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("loans.scoring.scorecard_changed", nameof(Scorecard), card.Id.ToString(), byUser,
            $$"""{"approve":{{approveThreshold}},"refer":{{referThreshold}},"declineIfListed":{{declineIfBureauListed.ToString().ToLower()}},"factors":{{factors.Count}}}"""), ct);
        return card;
    }

    public Task<LoanCreditScore?> GetLatestAsync(Guid loanId, CancellationToken ct)
        => db.CreditScores.AsNoTracking().Where(s => s.LoanId == loanId).OrderByDescending(s => s.ComputedAt).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<LoanCreditScore>> HistoryAsync(Guid loanId, CancellationToken ct)
        => await db.CreditScores.AsNoTracking().Where(s => s.LoanId == loanId).OrderByDescending(s => s.ComputedAt).ToListAsync(ct);

    /// <summary>Latest score per loan, for list views.</summary>
    public async Task<IReadOnlyDictionary<Guid, LoanCreditScore>> LatestForAsync(IReadOnlyCollection<Guid> loanIds, CancellationToken ct)
    {
        var rows = await db.CreditScores.AsNoTracking().Where(s => loanIds.Contains(s.LoanId)).OrderByDescending(s => s.ComputedAt).ToListAsync(ct);
        return rows.GroupBy(s => s.LoanId).ToDictionary(g => g.Key, g => g.First());
    }

    public async Task<LoanCreditScore> ScoreLoanAsync(Guid loanId, ScoreStage stage, Guid byUser, CancellationToken ct)
    {
        var loan = await db.Loans.FirstOrDefaultAsync(l => l.Id == loanId, ct) ?? throw new NotFoundException("Loan", loanId);
        if (loan.Status is LoanStatus.Active or LoanStatus.Closed or LoanStatus.Rejected)
            throw new DomainRuleException("loans.scoring.decided", $"Loan {loan.LoanNumber} is {loan.Status}; scoring applies before a decision.");
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == loan.ProductId, ct);
        var member = await members.FindAsync(loan.MemberId, ct) ?? throw new NotFoundException("Member", loan.MemberId);
        var summary = await savings.GetMemberSummaryAsync(loan.MemberId, ct);
        var card = await GetScorecardAsync(ct);

        var prior = await db.Loans.AsNoTracking().Where(l => l.MemberId == loan.MemberId && l.Id != loan.Id && l.Status != LoanStatus.Rejected).ToListAsync(ct);
        var today = clock.Today;
        var worstArrears = prior.Count == 0 ? 0 : prior.Max(l => WorstArrearsDays(l, product.GracePeriodDays, today));
        var currentArrears = prior.Where(l => l.Status == LoanStatus.Active).Sum(l => l.ArrearsAmount(today));
        decimal outstanding = 0;
        foreach (var l in prior.Where(l => l.Status == LoanStatus.Active && l.LedgerAccountNumber is not null))
            outstanding += (await ledger.FindAccountAsync(l.LedgerAccountNumber!, ct))?.Balance ?? 0;

        var report = await SafeBureauCheckAsync(new BureauQuery(member.NationalIdNumber, member.FullName, member.PhoneNumber), ct);
        var membershipMonths = Math.Max(0, (today.Year - member.JoinedAt.Year) * 12 + today.Month - member.JoinedAt.Month);

        var inputs = new ScoringInputs(loan.Amount, loan.Eligibility.MaxEligibleAmount, summary.BosaDeposits, outstanding, membershipMonths, summary.MonthsWithContributions,
            prior.Count, worstArrears, currentArrears, loan.AcceptedGuarantees, report.Status);
        var result = CreditScoringEngine.Score(card, inputs);

        var score = LoanCreditScore.Create(tenant.TenantId, loan.Id, stage, result, report.Status, report.Reference, report.Narrative, JsonSerializer.Serialize(result.Factors, Json), card.UpdatedAt, byUser, clock.UtcNow);
        db.CreditScores.Add(score);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("loans.scored", nameof(Loan), loan.Id.ToString(), byUser,
            $$"""{"loan":"{{loan.LoanNumber}}","stage":"{{stage}}","score":{{result.Score}},"grade":"{{result.Grade}}","recommendation":"{{result.Recommendation}}","bureau":"{{report.Status}}"}"""), ct);
        return score;
    }

    /// <summary>Worst arrears observed on a prior loan: today's figure for an active loan, the latest any instalment was settled for a closed one. A written-off loan is the worst case.</summary>
    private static int WorstArrearsDays(Loan l, int graceDays, DateOnly today) => l.Status switch
    {
        LoanStatus.Active => Math.Max(l.DaysInArrears(today, graceDays), l.Schedule.Count == 0 ? 0 : l.Schedule.Max(i => i.DaysLate(graceDays))),
        LoanStatus.WrittenOff => 9999,
        _ => l.Schedule.Count == 0 ? 0 : l.Schedule.Max(i => i.DaysLate(graceDays)),
    };

    private async Task<BureauReport> SafeBureauCheckAsync(BureauQuery query, CancellationToken ct)
    {
        try { return await bureau.CheckAsync(query, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Credit bureau {Bureau} lookup failed; scoring without a bureau result", bureau.Name);
            return new BureauReport(BureauStatus.Unavailable, null, null, $"{bureau.Name}: {ex.Message}", clock.UtcNow);
        }
    }

    public static IReadOnlyList<FactorScore> ParseFactors(string json)
        => JsonSerializer.Deserialize<List<FactorScore>>(json, Json) ?? [];
}
