using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Ledger.Application;
using Sacco.Modules.Ledger.Domain;
using Sacco.Modules.Ledger.Persistence;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Sacco.Shared.Http;
using Sacco.Shared.Ledger;
using Sacco.Shared.Savings;
using Sacco.Shared.Time;

namespace Sacco.Modules.Ledger.Endpoints;

public sealed record GlAccountResponse(Guid Id, string Code, string Name, GlAccountCategory Category, Segment Segment, EntryDirection NormalBalance, Guid? ParentId, bool IsPostable, bool IsControlAccount, bool IsActive, decimal Balance, string? Description);
public sealed record CreateGlAccountRequest(string Code, string Name, GlAccountCategory Category, Segment Segment, bool IsPostable, bool IsControlAccount, string? ParentCode, string? Description, EntryDirection? NormalBalance);

public sealed record JournalLineRequest(string GlAccountCode, Segment Segment, EntryDirection Direction, decimal Amount, string? LedgerAccountNumber, string? Narrative);
public sealed record CreateJournalRequest(string Reference, string Description, DateOnly? ValueDate, IReadOnlyList<JournalLineRequest> Lines);
public sealed record RejectJournalRequest(string Reason);
public sealed record ReverseJournalRequest(string Reason);

public sealed record JournalLineResponse(int LineNumber, string GlAccountCode, string? LedgerAccountNumber, Segment Segment, EntryDirection Direction, decimal Amount, string? Narrative);
public sealed record JournalResponse(Guid Id, string Reference, string Description, DateOnly ValueDate, string Source, JournalEntryStatus Status, decimal TotalAmount,
    Guid InitiatedByUserId, DateTimeOffset InitiatedAt, Guid? ApprovedByUserId, DateTimeOffset? PostedAt, string? RejectionReason, Guid? ReversalOfEntryId, Guid? ReversedByEntryId, Guid? BranchId, IReadOnlyList<JournalLineResponse> Lines);

public sealed class LedgerEndpoints : IModuleEndpoints
{
    public void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/ledger").WithTags("Ledger");

        // ---- Chart of accounts ----
        g.MapGet("/gl-accounts", async (LedgerDbContext db, Segment? segment, CancellationToken ct) =>
        {
            var q = db.GlAccounts.AsNoTracking();
            if (segment is Segment s) q = q.Where(a => a.Segment == s);
            var rows = await q.OrderBy(a => a.Code).Select(a => ToResponse(a)).ToListAsync(ct);
            return TypedResults.Ok(rows);
        }).RequirePermission(Permissions.Ledger.View).WithName("ListGlAccounts");

        g.MapGet("/gl-accounts/{code}", async Task<Results<Ok<GlAccountResponse>, NotFound>> (string code, LedgerDbContext db, CancellationToken ct) =>
        {
            var a = await db.GlAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Code == code, ct);
            return a is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(a));
        }).RequirePermission(Permissions.Ledger.View).WithName("GetGlAccount");

        g.MapPost("/gl-accounts", async (CreateGlAccountRequest req, LedgerDbContext db, Sacco.Shared.Tenancy.ITenantContext tenant, CancellationToken ct) =>
        {
            Guid? parentId = null;
            if (req.ParentCode is not null)
                parentId = (await db.GlAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Code == req.ParentCode, ct))?.Id
                           ?? throw new NotFoundException("GL account", req.ParentCode);
            var account = GlAccount.Create(Ids.New(), tenant.TenantId, req.Code, req.Name, req.Category, req.Segment, req.IsPostable, req.IsControlAccount, parentId, req.Description, req.NormalBalance);
            db.GlAccounts.Add(account);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (PostingEngine.IsUniqueViolation(ex, "code"))
            { throw new ConflictException("ledger.gl_account.duplicate_code", $"GL account code {req.Code} already exists."); }
            return TypedResults.Created($"/api/ledger/gl-accounts/{account.Code}", ToResponse(account));
        }).RequirePermission(Permissions.Ledger.ChartManage).WithName("CreateGlAccount");

        // ---- Sub-ledger accounts ----
        g.MapGet("/accounts/{accountNumber}", async Task<Results<Ok<LedgerAccountSnapshot>, NotFound>> (string accountNumber, ILedgerService ledger, CancellationToken ct) =>
        {
            var a = await ledger.FindAccountAsync(accountNumber, ct);
            return a is null ? TypedResults.NotFound() : TypedResults.Ok(a);
        }).RequirePermission(Permissions.Ledger.View).WithName("GetLedgerAccount");

        g.MapGet("/accounts/by-member/{memberId:guid}", async (Guid memberId, ILedgerService ledger, CancellationToken ct) =>
            TypedResults.Ok(await ledger.GetMemberAccountsAsync(memberId, ct)))
            .RequirePermission(Permissions.Ledger.View).WithName("GetMemberLedgerAccounts");

        g.MapGet("/accounts/{accountNumber}/statement", async Task<Results<Ok<AccountStatement>, NotFound>> (string accountNumber, LedgerQueries queries, IClock clock, DateOnly? from, DateOnly? to, CancellationToken ct) =>
        {
            var statement = await queries.StatementAsync(accountNumber, from ?? clock.Today.AddMonths(-3), to ?? clock.Today, ct);
            return statement is null ? TypedResults.NotFound() : TypedResults.Ok(statement);
        }).RequirePermission(Permissions.Ledger.View).WithName("GetAccountStatement");

        app.MapGet("/api/self/statements/{accountNumber}", async Task<Results<Ok<AccountStatement>, NotFound>> (string accountNumber, LedgerQueries queries, ILedgerService ledger, IBalanceVisibility visibility, IClock clock, ICurrentUser user, DateOnly? from, DateOnly? to, CancellationToken ct) =>
        {
            var account = await ledger.FindAccountAsync(accountNumber, ct);
            if (account is null || account.MemberId != user.RequireMemberId()) return TypedResults.NotFound(); // another member's account is invisible, not forbidden
            // Running balances would leak a fee-gated balance (ADR 0015): the member pays for a reveal window first.
            if (!await visibility.IsBalanceVisibleAsync(account.MemberId, accountNumber, ct))
                throw new DomainRuleException("savings.balance.locked", "This account's balance is shown after a paid balance enquiry.");
            var statement = await queries.StatementAsync(accountNumber, from ?? clock.Today.AddMonths(-3), to ?? clock.Today, ct);
            return statement is null ? TypedResults.NotFound() : TypedResults.Ok(statement);
        }).RequirePermission(Permissions.Self.StatementsView).WithTags("Self-service").WithName("GetMyStatement");

        g.MapPost("/accounts/{accountNumber}/status", async (string accountNumber, LedgerAccountStatus status, ILedgerService ledger, ICurrentUser user, CancellationToken ct) =>
        {
            await ledger.SetAccountStatusAsync(accountNumber, status, user.UserId, ct);
            return TypedResults.NoContent();
        }).RequirePermission(Permissions.Ledger.AccountsManage).WithName("SetLedgerAccountStatus");

        // ---- Journals (manual journals are maker-checker) ----
        g.MapGet("/journals", async (LedgerDbContext db, JournalEntryStatus? status, Guid? branchId, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 200);
            var q = db.JournalEntries.AsNoTracking();
            if (status is JournalEntryStatus st) q = q.Where(e => e.Status == st);
            if (branchId is Guid branch) q = q.Where(e => e.BranchId == branch);
            var total = await q.CountAsync(ct);
            var items = await q.OrderByDescending(e => e.InitiatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return TypedResults.Ok(new PagedResult<JournalResponse>(items.Select(ToResponse).ToList(), page, pageSize, total));
        }).RequirePermission(Permissions.Ledger.View).WithName("ListJournals");

        g.MapGet("/journals/{id:guid}", async Task<Results<Ok<JournalResponse>, NotFound>> (Guid id, LedgerDbContext db, CancellationToken ct) =>
        {
            var e = await db.JournalEntries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return e is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(e));
        }).RequirePermission(Permissions.Ledger.View).WithName("GetJournal");

        g.MapPost("/journals", async (CreateJournalRequest req, JournalWorkflow workflow, ICurrentUser user, IClock clock, CancellationToken ct) =>
        {
            var lines = req.Lines.Select(l => new PostingLine(l.GlAccountCode, l.Segment, l.Direction, l.Amount, l.LedgerAccountNumber, l.Narrative)).ToList();
            var entry = await workflow.CreatePendingAsync(req.Reference, req.Description, req.ValueDate ?? clock.Today, lines, user.UserId, ct);
            return TypedResults.Created($"/api/ledger/journals/{entry.Id}", ToResponse(entry));
        }).RequirePermission(Permissions.Ledger.JournalCreate).WithName("CreateJournal");

        g.MapPost("/journals/{id:guid}/approve", async (Guid id, JournalWorkflow workflow, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await workflow.ApproveAsync(id, user.UserId, ct))))
            .RequirePermission(Permissions.Ledger.JournalApprove).WithName("ApproveJournal");

        g.MapPost("/journals/{id:guid}/reject", async (Guid id, RejectJournalRequest req, JournalWorkflow workflow, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await workflow.RejectAsync(id, user.UserId, req.Reason, ct))))
            .RequirePermission(Permissions.Ledger.JournalApprove).WithName("RejectJournal");

        g.MapPost("/journals/{id:guid}/reverse", async (Guid id, ReverseJournalRequest req, JournalWorkflow workflow, ICurrentUser user, CancellationToken ct) =>
        {
            var reversal = await workflow.RequestReversalAsync(id, req.Reason, user.UserId, ct);
            return TypedResults.Created($"/api/ledger/journals/{reversal.Id}", ToResponse(reversal));
        }).RequirePermission(Permissions.Ledger.JournalReverse).WithName("ReverseJournal");

        // ---- Reports ----
        g.MapGet("/trial-balance", async (LedgerQueries queries, IClock clock, Segment? segment, DateOnly? asOf, CancellationToken ct) =>
            TypedResults.Ok(await queries.TrialBalanceAsync(segment, asOf ?? clock.Today, ct)))
            .RequirePermission(Permissions.Ledger.View).WithName("GetTrialBalance");

        g.MapGet("/reconciliation", async (LedgerQueries queries, CancellationToken ct) => TypedResults.Ok(await queries.ReconcileAsync(ct)))
            .RequirePermission(Permissions.Ledger.View).WithName("GetLedgerReconciliation");
    }

    private static GlAccountResponse ToResponse(GlAccount a) =>
        new(a.Id, a.Code, a.Name, a.Category, a.Segment, a.NormalBalance, a.ParentId, a.IsPostable, a.IsControlAccount, a.IsActive, a.Balance, a.Description);

    private static JournalResponse ToResponse(JournalEntry e) =>
        new(e.Id, e.Reference, e.Description, e.ValueDate, e.Source, e.Status, e.TotalAmount, e.InitiatedByUserId, e.InitiatedAt, e.ApprovedByUserId, e.PostedAt,
            e.RejectionReason, e.ReversalOfEntryId, e.ReversedByEntryId, e.BranchId,
            e.Lines.OrderBy(l => l.LineNumber).Select(l => new JournalLineResponse(l.LineNumber, l.GlAccountCode, l.LedgerAccountNumber, l.Segment, l.Direction, l.Amount, l.Narrative)).ToList());
}
