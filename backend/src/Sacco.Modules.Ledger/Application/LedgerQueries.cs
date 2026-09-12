using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Ledger.Domain;
using Sacco.Modules.Ledger.Persistence;
using Sacco.Shared.Domain;

namespace Sacco.Modules.Ledger.Application;

public sealed record TrialBalanceRow(string Code, string Name, GlAccountCategory Category, Segment Segment, decimal Debit, decimal Credit, decimal Balance, EntryDirection NormalBalance = EntryDirection.Debit, bool IsControlAccount = false);
public sealed record GlActivityRow(string Code, string Name, GlAccountCategory Category, Segment Segment, EntryDirection NormalBalance, decimal Debit, decimal Credit);
public sealed record TrialBalance(Segment? Segment, DateOnly AsOf, IReadOnlyList<TrialBalanceRow> Rows, decimal TotalDebits, decimal TotalCredits, bool IsBalanced);

public sealed record ReconciliationIssue(string Kind, string Code, decimal Expected, decimal Actual);
public sealed record ReconciliationReport(bool IsClean, int GlAccountsChecked, int ControlAccountsChecked, IReadOnlyList<ReconciliationIssue> Issues);

public sealed record StatementLine(DateOnly ValueDate, DateTimeOffset PostedAt, string Reference, string Description, string? Narrative, EntryDirection Direction, decimal Amount, decimal RunningBalance);
public sealed record AccountStatement(string AccountNumber, decimal OpeningBalance, decimal ClosingBalance, IReadOnlyList<StatementLine> Lines);

/// <summary>Read-side queries. Trial balance is computed from posted lines, not the running balance, so the two can be reconciled independently.</summary>
public sealed class LedgerQueries(LedgerDbContext db)
{
    public async Task<TrialBalance> TrialBalanceAsync(Segment? segment, DateOnly asOf, CancellationToken ct)
    {
        var lines = db.JournalLines.AsNoTracking()
            .Join(db.JournalEntries.AsNoTracking().Where(e => e.ValueDate <= asOf && (e.Status == JournalEntryStatus.Posted || e.Status == JournalEntryStatus.Reversed)),
                l => l.JournalEntryId, e => e.Id, (l, e) => l);
        if (segment is Segment s) lines = lines.Where(l => l.Segment == s);

        var sums = await lines
            .GroupBy(l => l.GlAccountId)
            .Select(g => new
            {
                GlAccountId = g.Key,
                Debit = g.Where(l => l.Direction == EntryDirection.Debit).Sum(l => l.Amount),
                Credit = g.Where(l => l.Direction == EntryDirection.Credit).Sum(l => l.Amount),
            })
            .ToDictionaryAsync(x => x.GlAccountId, ct);

        var accountsQuery = db.GlAccounts.AsNoTracking().Where(a => a.IsPostable);
        if (segment is Segment s2) accountsQuery = accountsQuery.Where(a => a.Segment == s2);
        var accounts = await accountsQuery.OrderBy(a => a.Code).ToListAsync(ct);

        var rows = accounts.Select(a =>
        {
            sums.TryGetValue(a.Id, out var sum);
            var debit = sum?.Debit ?? 0m;
            var credit = sum?.Credit ?? 0m;
            var balance = a.NormalBalance == EntryDirection.Debit ? debit - credit : credit - debit;
            return new TrialBalanceRow(a.Code, a.Name, a.Category, a.Segment, debit, credit, balance, a.NormalBalance, a.IsControlAccount);
        }).ToList();

        var totalDebits = rows.Sum(r => r.Debit);
        var totalCredits = rows.Sum(r => r.Credit);
        return new TrialBalance(segment, asOf, rows, totalDebits, totalCredits, totalDebits == totalCredits);
    }

    /// <summary>Debit/credit totals per postable GL account for value dates in [from, to].</summary>
    public async Task<IReadOnlyList<GlActivityRow>> ActivityAsync(DateOnly from, DateOnly to, Segment? segment, CancellationToken ct)
    {
        var lines = db.JournalLines.AsNoTracking()
            .Join(db.JournalEntries.AsNoTracking().Where(e => e.ValueDate >= from && e.ValueDate <= to && (e.Status == JournalEntryStatus.Posted || e.Status == JournalEntryStatus.Reversed)),
                l => l.JournalEntryId, e => e.Id, (l, e) => l);
        if (segment is Segment s) lines = lines.Where(l => l.Segment == s);
        var sums = await lines.GroupBy(l => l.GlAccountId)
            .Select(g => new { g.Key, Debit = g.Where(l => l.Direction == EntryDirection.Debit).Sum(l => l.Amount), Credit = g.Where(l => l.Direction == EntryDirection.Credit).Sum(l => l.Amount) })
            .ToDictionaryAsync(x => x.Key, ct);
        var accountsQuery = db.GlAccounts.AsNoTracking().Where(a => a.IsPostable);
        if (segment is Segment s2) accountsQuery = accountsQuery.Where(a => a.Segment == s2);
        var accounts = await accountsQuery.OrderBy(a => a.Code).ToListAsync(ct);
        return accounts.Select(a => { sums.TryGetValue(a.Id, out var x); return new GlActivityRow(a.Code, a.Name, a.Category, a.Segment, a.NormalBalance, x?.Debit ?? 0m, x?.Credit ?? 0m); }).ToList();
    }

    /// <summary>Verifies running balances against posted lines and control accounts against their sub-ledgers.</summary>
    public async Task<ReconciliationReport> ReconcileAsync(CancellationToken ct)
    {
        var issues = new List<ReconciliationIssue>();
        var accounts = await db.GlAccounts.AsNoTracking().Where(a => a.IsPostable).ToListAsync(ct);

        var postedLines = db.JournalLines.AsNoTracking()
            .Join(db.JournalEntries.AsNoTracking().Where(e => e.Status == JournalEntryStatus.Posted || e.Status == JournalEntryStatus.Reversed),
                l => l.JournalEntryId, e => e.Id, (l, e) => l);

        var glSums = await postedLines.GroupBy(l => l.GlAccountId)
            .Select(g => new { g.Key, Debit = g.Where(l => l.Direction == EntryDirection.Debit).Sum(l => l.Amount), Credit = g.Where(l => l.Direction == EntryDirection.Credit).Sum(l => l.Amount) })
            .ToDictionaryAsync(x => x.Key, ct);

        foreach (var a in accounts)
        {
            glSums.TryGetValue(a.Id, out var sum);
            var expected = a.NormalBalance == EntryDirection.Debit ? (sum?.Debit ?? 0) - (sum?.Credit ?? 0) : (sum?.Credit ?? 0) - (sum?.Debit ?? 0);
            if (expected != a.Balance) issues.Add(new ReconciliationIssue("gl_running_balance", a.Code, expected, a.Balance));
        }

        var subSums = await db.LedgerAccounts.AsNoTracking().GroupBy(s => s.ControlGlAccountId)
            .Select(g => new { g.Key, Total = g.Sum(s => s.Balance) }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        var controls = accounts.Where(a => a.IsControlAccount).ToList();
        foreach (var c in controls)
        {
            subSums.TryGetValue(c.Id, out var total);
            if (total != c.Balance) issues.Add(new ReconciliationIssue("control_vs_subledger", c.Code, total, c.Balance));
        }

        var subLineSums = await postedLines.Where(l => l.LedgerAccountId != null).GroupBy(l => l.LedgerAccountId!.Value)
            .Select(g => new { g.Key, Debit = g.Where(l => l.Direction == EntryDirection.Debit).Sum(l => l.Amount), Credit = g.Where(l => l.Direction == EntryDirection.Credit).Sum(l => l.Amount) })
            .ToDictionaryAsync(x => x.Key, ct);
        var subs = await db.LedgerAccounts.AsNoTracking()
            .Join(db.GlAccounts.AsNoTracking(), s => s.ControlGlAccountId, g => g.Id, (s, g) => new { s.Id, s.AccountNumber, s.Balance, g.NormalBalance })
            .ToListAsync(ct);
        foreach (var s in subs)
        {
            subLineSums.TryGetValue(s.Id, out var sum);
            var expected = s.NormalBalance == EntryDirection.Debit ? (sum?.Debit ?? 0) - (sum?.Credit ?? 0) : (sum?.Credit ?? 0) - (sum?.Debit ?? 0);
            if (expected != s.Balance) issues.Add(new ReconciliationIssue("subledger_running_balance", s.AccountNumber, expected, s.Balance));
        }

        return new ReconciliationReport(issues.Count == 0, accounts.Count, controls.Count, issues);
    }

    public async Task<AccountStatement?> StatementAsync(string accountNumber, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var account = await db.LedgerAccounts.AsNoTracking()
            .Join(db.GlAccounts.AsNoTracking(), s => s.ControlGlAccountId, g => g.Id, (s, g) => new { s.Id, s.AccountNumber, g.NormalBalance })
            .FirstOrDefaultAsync(x => x.AccountNumber == accountNumber, ct);
        if (account is null) return null;

        var entries = db.JournalEntries.AsNoTracking().Where(e => e.Status == JournalEntryStatus.Posted || e.Status == JournalEntryStatus.Reversed);
        var all = await db.JournalLines.AsNoTracking().Where(l => l.LedgerAccountId == account.Id)
            .Join(entries, l => l.JournalEntryId, e => e.Id, (l, e) => new { l, e })
            .Where(x => x.e.ValueDate <= to)
            .OrderBy(x => x.e.ValueDate).ThenBy(x => x.e.PostedAt).ThenBy(x => x.l.LineNumber)
            .ToListAsync(ct);

        decimal Signed(EntryDirection d, decimal amt) => d == account.NormalBalance ? amt : -amt;
        var opening = all.Where(x => x.e.ValueDate < from).Sum(x => Signed(x.l.Direction, x.l.Amount));
        var running = opening;
        var lines = new List<StatementLine>();
        foreach (var x in all.Where(x => x.e.ValueDate >= from))
        {
            running += Signed(x.l.Direction, x.l.Amount);
            lines.Add(new StatementLine(x.e.ValueDate, x.e.PostedAt ?? x.e.InitiatedAt, x.e.Reference, x.e.Description, x.l.Narrative, x.l.Direction, x.l.Amount, running));
        }
        return new AccountStatement(accountNumber, opening, running, lines);
    }
}
