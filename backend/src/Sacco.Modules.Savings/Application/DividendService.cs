using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sacco.Modules.Savings.Domain;
using Sacco.Modules.Savings.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Savings;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Savings.Application;

/// <summary>
/// Dividend/interest-rebate skeleton. Declare (maker) → Approve (checker; posts the appropriation
/// to the GL) → Pay (credits each member's FOSA account net of withholding tax).
/// </summary>
public sealed class DividendService(SavingsDbContext db, ILedgerService ledger, ITenantContext tenant, IClock clock, IAuditLogger audit, IOptions<SavingsSettings> options)
{
    private SavingsSettings Settings => options.Value;

    public async Task<DividendDeclaration> DeclareAsync(int financialYear, int shareRateBps, int depositRateBps, Guid byUser, CancellationToken ct)
    {
        if (await db.Dividends.AnyAsync(d => d.FinancialYear == financialYear && d.Status != DividendStatus.Rejected, ct))
            throw new ConflictException("savings.dividend.exists", $"A dividend declaration for FY{financialYear} already exists.");

        // Pro-rata by time: each member's basis is the average daily balance over the financial year, read from the
        // ledger statement, so a deposit made in December earns a twelfth of one made in January.
        var yearStart = new DateOnly(financialYear, 1, 1);
        var yearEnd = new DateOnly(financialYear, 12, 31);
        if (yearEnd >= clock.Today) throw new DomainRuleException("savings.dividend.year_open", $"FY{financialYear} has not ended; dividends are declared after year end.");
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.Status == SavingsAccountStatus.Active).ToListAsync(ct);
        var drafts = new List<DividendLineDraft>();
        foreach (var group in accounts.GroupBy(a => a.MemberId))
        {
            var shares = group.FirstOrDefault(a => a.Kind == ProductKind.Shares);
            var deposits = group.FirstOrDefault(a => a.Kind == ProductKind.BosaDeposit);
            var fosa = group.FirstOrDefault(a => a.Kind == ProductKind.FosaCurrent);
            var shareBalance = shares is null ? 0m : await AverageDailyBalanceAsync(shares.AccountNumber, yearStart, yearEnd, ct);
            var depositBalance = deposits is null ? 0m : await AverageDailyBalanceAsync(deposits.AccountNumber, yearStart, yearEnd, ct);
            drafts.Add(new DividendLineDraft(group.Key, shares?.AccountNumber, shareBalance, deposits?.AccountNumber, depositBalance, fosa?.AccountNumber));
        }

        var declaration = DividendDeclaration.Declare(Ids.New(), tenant.TenantId, financialYear, shareRateBps, depositRateBps, Settings.DividendWithholdingTaxBps, byUser, clock.UtcNow, drafts);
        db.Dividends.Add(declaration);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.dividend.declared", nameof(DividendDeclaration), declaration.Id.ToString(), byUser,
            $$"""{"year":{{financialYear}},"shareRateBps":{{shareRateBps}},"depositRateBps":{{depositRateBps}},"totalShareDividend":{{declaration.TotalShareDividend}},"totalDepositInterest":{{declaration.TotalDepositInterest}}}"""), ct);
        return declaration;
    }

    /// <summary>Time-weighted average of the ledger balance over [from, to], from the account statement.</summary>
    public async Task<decimal> AverageDailyBalanceAsync(string accountNumber, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var statement = await ledger.GetStatementAsync(accountNumber, from, to, ct);
        return statement is null ? 0m : AverageDailyBalance(statement.OpeningBalance, statement.Lines.Select(l => (l.ValueDate, l.RunningBalance)), from, to);
    }

    public static decimal AverageDailyBalance(decimal openingBalance, IEnumerable<(DateOnly Date, decimal RunningBalance)> movements, DateOnly from, DateOnly to)
    {
        var days = to.DayNumber - from.DayNumber + 1;
        if (days <= 0) return 0m;
        decimal weighted = 0, balance = openingBalance;
        var cursor = from;
        foreach (var group in movements.Where(m => m.Date >= from && m.Date <= to).GroupBy(m => m.Date).OrderBy(g => g.Key))
        {
            weighted += balance * (group.Key.DayNumber - cursor.DayNumber);
            balance = group.Last().RunningBalance;
            cursor = group.Key;
        }
        weighted += balance * (to.DayNumber - cursor.DayNumber + 1);
        return decimal.Round(weighted / days, 2, MidpointRounding.ToEven);
    }

    public async Task<DividendDeclaration> GetAsync(Guid id, CancellationToken ct)
        => await db.Dividends.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Dividend declaration", id);

    /// <summary>Checker approval posts: Dr Retained earnings / Cr Dividends payable, Dr Interest expense / Cr Interest payable (BOSA).</summary>
    public async Task<DividendDeclaration> ApproveAsync(Guid id, Guid byUser, CancellationToken ct)
    {
        var d = await GetAsync(id, ct);
        d.Approve(byUser, clock.UtcNow);

        var depositProduct = await db.Products.FirstOrDefaultAsync(p => p.Kind == ProductKind.BosaDeposit, ct);
        var lines = new List<PostingLine>();
        if (d.TotalShareDividend > 0)
        {
            lines.Add(new PostingLine(Settings.RetainedEarningsGl, Segment.Bosa, EntryDirection.Debit, d.TotalShareDividend, Narrative: $"FY{d.FinancialYear} dividend appropriation"));
            lines.Add(new PostingLine(Settings.DividendsPayableGl, Segment.Bosa, EntryDirection.Credit, d.TotalShareDividend, Narrative: $"FY{d.FinancialYear} dividend payable"));
        }
        if (d.TotalDepositInterest > 0)
        {
            var expenseGl = depositProduct?.InterestExpenseGlAccountCode ?? "5000";
            lines.Add(new PostingLine(expenseGl, Segment.Bosa, EntryDirection.Debit, d.TotalDepositInterest, Narrative: $"FY{d.FinancialYear} interest on deposits"));
            lines.Add(new PostingLine(Settings.InterestPayableGl, Segment.Bosa, EntryDirection.Credit, d.TotalDepositInterest, Narrative: $"FY{d.FinancialYear} interest payable"));
        }
        await ledger.PostAsync(new PostingRequest($"DIV-DECL:{d.FinancialYear}", $"FY{d.FinancialYear} dividend and deposit interest declaration", clock.Today, "Savings", byUser, lines), ct);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.dividend.approved", nameof(DividendDeclaration), id.ToString(), byUser, $$"""{"declaredBy":"{{d.DeclaredByUserId}}"}"""), ct);
        return d;
    }

    public async Task<DividendDeclaration> RejectAsync(Guid id, string reason, Guid byUser, CancellationToken ct)
    {
        var d = await GetAsync(id, ct);
        d.Reject(byUser, reason);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.dividend.rejected", nameof(DividendDeclaration), id.ToString(), byUser, $$"""{"reason":"{{reason.Replace("\"", "'")}}"}"""), ct);
        return d;
    }

    /// <summary>Pays each line into the member's FOSA account (net of WHT). Members without a FOSA account remain payable. Idempotent per line.</summary>
    public async Task<DividendDeclaration> PayAsync(Guid id, Guid byUser, CancellationToken ct)
    {
        var d = await GetAsync(id, ct);
        if (d.Status != DividendStatus.Approved) throw new DomainRuleException("savings.dividend.not_approved", $"Declaration is {d.Status}.");
        var fosaProduct = await db.Products.FirstOrDefaultAsync(p => p.Kind == ProductKind.FosaCurrent, ct) ?? throw new DomainRuleException("savings.dividend.no_fosa_product", "No FOSA product exists to pay into.");

        foreach (var line in d.Lines.Where(l => !l.IsPaid && l.PayoutAccountNumber is not null))
        {
            var reference = $"DIV-PAY:{d.FinancialYear}:{line.MemberId:N}";
            var lines = new List<PostingLine>();
            if (line.ShareDividend > 0) lines.Add(new PostingLine(Settings.DividendsPayableGl, Segment.Bosa, EntryDirection.Debit, line.ShareDividend, Narrative: "Dividend paid"));
            if (line.DepositInterest > 0) lines.Add(new PostingLine(Settings.InterestPayableGl, Segment.Bosa, EntryDirection.Debit, line.DepositInterest, Narrative: "Deposit interest paid"));
            if (line.WithholdingTax > 0) lines.Add(new PostingLine(Settings.WithholdingTaxPayableGl, Segment.Bosa, EntryDirection.Credit, line.WithholdingTax, Narrative: "Withholding tax"));
            lines.AddRange(PostingBuilder.Bridge(Settings, Segment.Bosa, Segment.Fosa, line.NetPayable, "Dividend payout"));
            lines.Add(new PostingLine(fosaProduct.ControlGlAccountCode, Segment.Fosa, EntryDirection.Credit, line.NetPayable, line.PayoutAccountNumber, $"FY{d.FinancialYear} dividend/interest"));
            try
            {
                await ledger.PostAsync(new PostingRequest(reference, $"FY{d.FinancialYear} dividend payout", clock.Today, "Savings", byUser, lines), ct);
            }
            catch (ConflictException) { /* already posted in an earlier, interrupted run */ }
            line.MarkPaid(reference);
        }
        d.MarkPaid(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.dividend.paid", nameof(DividendDeclaration), id.ToString(), byUser, $$"""{"lines":{{d.Lines.Count(l => l.IsPaid)}}}"""), ct);
        return d;
    }
}
