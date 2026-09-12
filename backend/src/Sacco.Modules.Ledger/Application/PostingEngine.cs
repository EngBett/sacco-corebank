using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sacco.Modules.Ledger.Domain;
using Sacco.Modules.Ledger.Persistence;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Ledger.Application;

/// <summary>
/// The only code path that changes an account balance. Concurrency safety comes from
/// Postgres row-level atomic conditional UPDATEs executed inside the caller's transaction
/// (ADR 0004) — never from a distributed lock. Accounts are always touched in ascending id
/// order (sub-ledger first, then GL) so concurrent postings cannot deadlock.
/// </summary>
public sealed class PostingEngine(LedgerDbContext db, ITenantContext tenant)
{
    public const string PostgresUniqueViolation = "23505";

    /// <summary>Resolves account codes to ids and validates every line against the chart of accounts.</summary>
    public async Task<IReadOnlyList<JournalLineDraft>> ResolveLinesAsync(IReadOnlyList<PostingLine> lines, CancellationToken ct)
    {
        if (lines.Count == 0)
            throw new DomainRuleException("ledger.journal.too_few_lines", "A journal needs at least one debit and one credit line.");

        var glCodes = lines.Select(l => l.GlAccountCode).Distinct().ToList();
        var glAccounts = await db.GlAccounts.AsNoTracking().Where(a => glCodes.Contains(a.Code)).ToDictionaryAsync(a => a.Code, ct);

        var accountNumbers = lines.Where(l => l.LedgerAccountNumber is not null).Select(l => l.LedgerAccountNumber!).Distinct().ToList();
        var ledgerAccounts = accountNumbers.Count == 0
            ? new Dictionary<string, LedgerAccount>()
            : await db.LedgerAccounts.AsNoTracking().Where(a => accountNumbers.Contains(a.AccountNumber)).ToDictionaryAsync(a => a.AccountNumber, ct);

        var drafts = new List<JournalLineDraft>(lines.Count);
        foreach (var line in lines)
        {
            if (!glAccounts.TryGetValue(line.GlAccountCode, out var gl))
                throw new NotFoundException("GL account", line.GlAccountCode);
            if (!gl.IsActive)
                throw new DomainRuleException("ledger.gl_account.inactive", $"GL account {gl.Code} is inactive.");
            if (!gl.IsPostable)
                throw new DomainRuleException("ledger.gl_account.not_postable", $"GL account {gl.Code} is a header account and cannot be posted to.");
            if (line.Segment != gl.Segment)
                throw new DomainRuleException("ledger.segment_mismatch",
                    $"Line for GL account {gl.Code} is tagged {line.Segment} but the account is {gl.Segment}. The FOSA/BOSA tag must match explicitly.");

            LedgerAccount? sub = null;
            if (line.LedgerAccountNumber is not null)
            {
                if (!ledgerAccounts.TryGetValue(line.LedgerAccountNumber, out sub))
                    throw new NotFoundException("Ledger account", line.LedgerAccountNumber);
                if (sub.ControlGlAccountId != gl.Id)
                    throw new DomainRuleException("ledger.control_mismatch",
                        $"Account {sub.AccountNumber} does not belong to control GL account {gl.Code}.");
                if (sub.Segment != line.Segment)
                    throw new DomainRuleException("ledger.segment_mismatch",
                        $"Account {sub.AccountNumber} is {sub.Segment} but the line is tagged {line.Segment}.");
                if (sub.Status == LedgerAccountStatus.Closed)
                    throw new DomainRuleException("ledger.account.closed", $"Account {sub.AccountNumber} is closed.");
            }
            else if (gl.IsControlAccount)
            {
                throw new DomainRuleException("ledger.control_requires_subaccount",
                    $"GL account {gl.Code} is a control account; a line against it must name the member sub-account.");
            }

            drafts.Add(new JournalLineDraft(gl.Id, gl.Code, sub?.Id, sub?.AccountNumber, line.Segment, line.Direction, line.Amount, line.Narrative));
        }
        return drafts;
    }

    /// <summary>
    /// Applies a posted entry's lines to running balances. Must be called inside the same
    /// database transaction that persisted the entry with status Posted.
    /// </summary>
    public async Task ApplyBalancesAsync(JournalEntry entry, CancellationToken ct)
    {
        if (entry.Status != JournalEntryStatus.Posted)
            throw new InvalidOperationException("Only posted entries affect balances.");

        var tenantId = tenant.TenantId;
        var glIds = entry.Lines.Select(l => l.GlAccountId).Distinct().ToList();
        var normalSides = await db.GlAccounts.AsNoTracking()
            .Where(a => glIds.Contains(a.Id))
            .Select(a => new { a.Id, a.NormalBalance })
            .ToDictionaryAsync(a => a.Id, a => a.NormalBalance, ct);

        // 1. Sub-ledger accounts (the ones that can be overdrawn) — conditional atomic update.
        var subDeltas = entry.Lines
            .Where(l => l.LedgerAccountId is not null)
            .GroupBy(l => l.LedgerAccountId!.Value)
            .Select(g => new
            {
                Id = g.Key,
                AccountNumber = g.First().LedgerAccountNumber!,
                Delta = g.Sum(l => l.Direction == normalSides[l.GlAccountId] ? l.Amount : -l.Amount),
            })
            .OrderBy(x => x.Id)
            .ToList();

        foreach (var s in subDeltas)
        {
            // Debits (negative delta) require an Active/Dormant account with enough *available* balance.
            // Credits are accepted on any account that is not Closed. Row lock is taken by the UPDATE itself.
            var affected = await db.Database.ExecuteSqlAsync($"""
                UPDATE ledger.ledger_accounts
                   SET balance = balance + {s.Delta}
                 WHERE id = {s.Id}
                   AND tenant_id = {tenantId}
                   AND status <> {(int)LedgerAccountStatus.Closed}
                   AND ({s.Delta} >= 0 OR (status IN ({(int)LedgerAccountStatus.Active}, {(int)LedgerAccountStatus.Dormant}) AND balance - held_amount + {s.Delta} >= 0))
                """, ct);

            if (affected == 1) continue;

            var current = await db.LedgerAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == s.Id, ct)
                          ?? throw new NotFoundException("Ledger account", s.AccountNumber);
            if (current.Status is LedgerAccountStatus.Closed or LedgerAccountStatus.Frozen)
                throw new DomainRuleException("ledger.account.not_active", $"Account {current.AccountNumber} is {current.Status} and cannot be debited.");
            throw new InsufficientFundsException(current.AccountNumber, -s.Delta);
        }

        // 2. GL accounts — unconditional atomic increment (GL balances may legitimately go either side).
        var glDeltas = entry.Lines
            .GroupBy(l => l.GlAccountId)
            .Select(g => new { Id = g.Key, Delta = g.Sum(l => l.Direction == normalSides[g.Key] ? l.Amount : -l.Amount) })
            .OrderBy(x => x.Id)
            .ToList();

        foreach (var g in glDeltas)
        {
            var affected = await db.Database.ExecuteSqlAsync($"""
                UPDATE ledger.gl_accounts
                   SET balance = balance + {g.Delta}
                 WHERE id = {g.Id} AND tenant_id = {tenantId} AND is_active = TRUE
                """, ct);
            if (affected != 1)
                throw new DomainRuleException("ledger.gl_account.inactive", $"GL account {g.Id} is inactive or missing.");
        }
    }

    public static bool IsUniqueViolation(DbUpdateException ex, string? constraintFragment = null)
        => ex.InnerException is PostgresException pg
           && pg.SqlState == PostgresUniqueViolation
           && (constraintFragment is null || (pg.ConstraintName?.Contains(constraintFragment, StringComparison.OrdinalIgnoreCase) ?? false));
}
