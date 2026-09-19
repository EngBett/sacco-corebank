using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sacco.Modules.Ledger.Domain;
using Sacco.Modules.Ledger.Persistence;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Ledger.Application;

/// <summary>Implementation of the Ledger module's public contract for other modules.</summary>
public sealed class LedgerService(LedgerDbContext db, PostingEngine engine, LedgerQueries queries, ITenantContext tenant, ICurrentUser currentUser, IClock clock, ILogger<LedgerService> logger) : ILedgerService
{
    public async Task<PostingResult> PostAsync(PostingRequest request, CancellationToken ct)
    {
        // Idempotency fast path: the unique index on (tenant, reference) is the guarantee; this check just keeps replays quiet.
        if (await db.JournalEntries.AsNoTracking().AnyAsync(e => e.Reference == request.Reference, ct))
            throw new ConflictException("ledger.journal.duplicate_reference", $"A journal with reference '{request.Reference}' already exists.");

        var drafts = await engine.ResolveLinesAsync(request.Lines, ct);
        var entry = JournalEntry.Create(Ids.New(), tenant.TenantId, request.Reference, request.Description, request.ValueDate,
            request.Source, request.PostedByUserId, clock.UtcNow, drafts, JournalEntryStatus.Posted, branchId: request.BranchId ?? currentUser.BranchId);

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.JournalEntries.Add(entry);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (PostingEngine.IsUniqueViolation(ex, "reference"))
            {
                db.Entry(entry).State = EntityState.Detached;
                throw new ConflictException("ledger.journal.duplicate_reference",
                    $"A journal with reference '{request.Reference}' already exists.");
            }

            await engine.ApplyBalancesAsync(entry, ct);
            await tx.CommitAsync(ct);

            logger.LogInformation("Posted journal {Reference} ({Source}) for {Amount:N2}", entry.Reference, entry.Source, entry.TotalAmount);
            return new PostingResult(entry.Id, entry.Reference, entry.TotalAmount);
        });
    }

    public async Task<LedgerAccountSnapshot> OpenAccountAsync(OpenLedgerAccountRequest request, CancellationToken ct)
    {
        var control = await db.GlAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Code == request.ControlGlAccountCode, ct)
                      ?? throw new NotFoundException("GL account", request.ControlGlAccountCode);
        if (control.Segment != request.Segment)
            throw new DomainRuleException("ledger.segment_mismatch",
                $"Control account {control.Code} is {control.Segment}; the account being opened is tagged {request.Segment}.");

        var account = LedgerAccount.Open(Ids.New(), tenant.TenantId, request.AccountNumber, request.MemberId, control, request.Kind, request.ProductCode, request.OpenedByUserId, clock.UtcNow);
        db.LedgerAccounts.Add(account);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostingEngine.IsUniqueViolation(ex, "account_number"))
        {
            throw new ConflictException("ledger.account.duplicate_number", $"Account number {request.AccountNumber} already exists.");
        }
        return Map(account, control.Code);
    }

    public async Task<LedgerAccountSnapshot?> FindAccountAsync(string accountNumber, CancellationToken ct)
    {
        var row = await db.LedgerAccounts.AsNoTracking()
            .Where(a => a.AccountNumber == accountNumber)
            .Join(db.GlAccounts.AsNoTracking(), a => a.ControlGlAccountId, g => g.Id, (a, g) => new { a, g.Code })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : Map(row.a, row.Code);
    }

    public async Task<IReadOnlyList<LedgerAccountSnapshot>> GetMemberAccountsAsync(Guid memberId, CancellationToken ct)
    {
        var rows = await db.LedgerAccounts.AsNoTracking()
            .Where(a => a.MemberId == memberId)
            .Join(db.GlAccounts.AsNoTracking(), a => a.ControlGlAccountId, g => g.Id, (a, g) => new { a, g.Code })
            .OrderBy(x => x.a.OpenedAt)
            .ToListAsync(ct);
        return rows.Select(r => Map(r.a, r.Code)).ToList();
    }

    public async Task SetAccountStatusAsync(string accountNumber, LedgerAccountStatus status, Guid byUserId, CancellationToken ct)
    {
        var account = await db.LedgerAccounts.FirstOrDefaultAsync(a => a.AccountNumber == accountNumber, ct)
                      ?? throw new NotFoundException("Ledger account", accountNumber);
        account.SetStatus(status, clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AccountStatementSnapshot?> GetStatementAsync(string accountNumber, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var st = await queries.StatementAsync(accountNumber, from, to, ct);
        return st is null ? null : new AccountStatementSnapshot(st.AccountNumber, st.OpeningBalance, st.ClosingBalance,
            st.Lines.Select(l => new StatementLineSnapshot(l.ValueDate, l.Reference, l.Description, l.Narrative, l.Direction, l.Amount, l.RunningBalance)).ToList());
    }

    public async Task<TrialBalanceSnapshot> GetTrialBalanceAsync(Segment? segment, DateOnly asOf, CancellationToken ct)
    {
        var tb = await queries.TrialBalanceAsync(segment, asOf, ct);
        return new TrialBalanceSnapshot(tb.Segment, tb.AsOf, tb.Rows.Select(r => new TrialBalanceLineSnapshot(r.Code, r.Name, r.Category.ToString(), r.Segment, r.NormalBalance, r.IsControlAccount, r.Debit, r.Credit, r.Balance)).ToList(), tb.TotalDebits, tb.TotalCredits, tb.IsBalanced);
    }

    public async Task<IReadOnlyList<GlActivitySnapshot>> GetGlActivityAsync(DateOnly from, DateOnly to, Segment? segment, CancellationToken ct)
        => (await queries.ActivityAsync(from, to, segment, ct)).Select(r => new GlActivitySnapshot(r.Code, r.Name, r.Category.ToString(), r.Segment, r.NormalBalance, r.Debit, r.Credit)).ToList();

    public async Task<LedgerReconciliationSnapshot> ReconcileAsync(CancellationToken ct)
    {
        var r = await queries.ReconcileAsync(ct);
        return new LedgerReconciliationSnapshot(r.IsClean, r.GlAccountsChecked, r.ControlAccountsChecked, r.Issues.Select(i => $"{i.Kind} {i.Code}: expected {i.Expected:N2}, actual {i.Actual:N2}").ToList());
    }

    public async Task<decimal> GetGlBalanceAsync(string glAccountCode, CancellationToken ct)
        => (await db.GlAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Code == glAccountCode, ct))?.Balance
           ?? throw new NotFoundException("GL account", glAccountCode);

    public async Task<GlAccountSnapshot?> FindGlAccountAsync(string glAccountCode, CancellationToken ct)
        => await db.GlAccounts.AsNoTracking().Where(a => a.Code == glAccountCode)
            .Select(a => new GlAccountSnapshot(a.Code, a.Name, a.Category.ToString(), a.Segment, a.IsPostable, a.IsControlAccount, a.IsActive))
            .FirstOrDefaultAsync(ct);

    public async Task PlaceHoldAsync(string accountNumber, decimal amount, string reason, CancellationToken ct)
    {
        if (amount <= 0) throw new DomainRuleException("ledger.hold.non_positive", "Hold amount must be positive.");
        var affected = await db.Database.ExecuteSqlAsync($"""
            UPDATE ledger.ledger_accounts
               SET held_amount = held_amount + {amount}
             WHERE account_number = {accountNumber} AND tenant_id = {tenant.TenantId}
               AND status = {(int)LedgerAccountStatus.Active}
               AND balance - held_amount >= {amount}
            """, ct);
        if (affected == 1) return;
        var account = await db.LedgerAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountNumber == accountNumber, ct)
                      ?? throw new NotFoundException("Ledger account", accountNumber);
        if (account.Status != LedgerAccountStatus.Active)
            throw new DomainRuleException("ledger.account.not_active", $"Account {accountNumber} is {account.Status}.");
        throw new InsufficientFundsException(accountNumber, amount);
    }

    public async Task ReleaseHoldAsync(string accountNumber, decimal amount, string reason, CancellationToken ct)
    {
        if (amount <= 0) throw new DomainRuleException("ledger.hold.non_positive", "Hold amount must be positive.");
        var affected = await db.Database.ExecuteSqlAsync($"""
            UPDATE ledger.ledger_accounts
               SET held_amount = held_amount - {amount}
             WHERE account_number = {accountNumber} AND tenant_id = {tenant.TenantId}
               AND held_amount >= {amount}
            """, ct);
        if (affected != 1)
            throw new DomainRuleException("ledger.hold.release_exceeds", $"Cannot release {amount:N2} from {accountNumber}: not that much is held.");
    }

    internal static LedgerAccountSnapshot Map(LedgerAccount a, string controlCode) =>
        new(a.Id, a.AccountNumber, a.MemberId, controlCode, a.Segment, a.Kind, a.Status, a.ProductCode, a.Balance, a.HeldAmount, a.AvailableBalance, a.OpenedAt);
}
