using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Sacco.Modules.Savings.Application;
using Sacco.Modules.Savings.Domain;
using Sacco.Modules.Savings.Persistence;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Sacco.Shared.Http;
using Sacco.Shared.Ledger;
using Sacco.Shared.Savings;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Savings.Endpoints;

public sealed record ProductResponse(Guid Id, string Code, string Name, string? Description, ProductKind Kind, Segment Segment, string ControlGlAccountCode, string AccountSuffix, decimal MinimumOpeningDeposit, decimal MinimumBalance,
    bool AllowsWithdrawals, int WithdrawalNoticeDays, decimal TellerWithdrawalLimit, decimal WithdrawalFee, string? FeeIncomeGlAccountCode, int InterestRateBps, string? InterestExpenseGlAccountCode, int? TermMonths, bool IsActive);
public sealed record CreateProductRequest(string Code, string Name, string? Description, ProductKind Kind, Segment Segment, string ControlGlAccountCode, string AccountSuffix, decimal MinimumOpeningDeposit, decimal MinimumBalance,
    bool AllowsWithdrawals, int WithdrawalNoticeDays, decimal TellerWithdrawalLimit, decimal WithdrawalFee, string? FeeIncomeGlAccountCode, int InterestRateBps, string? InterestExpenseGlAccountCode, int? TermMonths);
public sealed record UpdateProductRequest(string Name, string? Description, decimal MinimumOpeningDeposit, decimal MinimumBalance, int WithdrawalNoticeDays, decimal TellerWithdrawalLimit, decimal WithdrawalFee, string? FeeIncomeGlAccountCode, int InterestRateBps, string? InterestExpenseGlAccountCode, bool IsActive);

public sealed record PublicSavingsProduct(string Code, string Name, string? Description, ProductKind Kind, Segment Segment, decimal MinimumOpeningDeposit, decimal MinimumBalance, bool AllowsWithdrawals, int WithdrawalNoticeDays, int InterestRateBps, int? TermMonths);
public sealed record OpenAccountRequest(Guid MemberId, string ProductCode);
public sealed record OpenFixedDepositRequest(Guid MemberId, string ProductCode, decimal Principal, string FromAccountNumber);
public sealed record SavingsAccountResponse(Guid Id, string AccountNumber, Guid MemberId, string ProductCode, ProductKind Kind, Segment Segment, SavingsAccountStatus Status, DateTimeOffset OpenedAt,
    decimal Balance, decimal HeldAmount, decimal AvailableBalance, decimal? Principal, int? TermMonths, int? InterestRateBps, DateOnly? MaturityDate, string? PayoutAccountNumber);
public sealed record DepositRequest(decimal Amount, DepositChannel Channel, string Reference, string? Narrative);
public sealed record WithdrawalRequestDto(decimal Amount, PayoutChannel Channel, string? Destination, string? Narrative);
public sealed record WithdrawalResponse(Guid Id, string AccountNumber, Guid MemberId, decimal Amount, decimal Fee, PayoutChannel Channel, string? PayoutDestination, WithdrawalStatus Status,
    Guid RequestedByUserId, DateTimeOffset RequestedAt, DateOnly NoticeExpiresOn, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAt, Guid? PaidByUserId, DateTimeOffset? PaidAt, string? JournalReference, string? RejectionReason, string? Narrative);
public sealed record ReasonDto(string Reason);
public sealed record DeclareDividendRequest(int FinancialYear, int ShareRateBps, int DepositRateBps);
public sealed record DividendLineResponse(Guid MemberId, string? SharesAccountNumber, decimal ShareBalance, decimal ShareDividend, string? DepositsAccountNumber, decimal DepositBalance, decimal DepositInterest, decimal WithholdingTax, decimal NetPayable, string? PayoutAccountNumber, bool IsPaid);
public sealed record DividendResponse(Guid Id, int FinancialYear, int ShareDividendRateBps, int DepositInterestRateBps, int WithholdingTaxBps, DividendStatus Status, Guid DeclaredByUserId, DateTimeOffset DeclaredAt, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAt, DateTimeOffset? PaidAt,
    decimal TotalShareDividend, decimal TotalDepositInterest, decimal TotalWithholdingTax, IReadOnlyList<DividendLineResponse> Lines);

public sealed class SavingsEndpoints(IHostEnvironment env) : IModuleEndpoints
{
    public void Map(IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/api/savings/products").WithTags("Savings products");
        products.MapGet("", async (SavingsDbContext db, CancellationToken ct) => TypedResults.Ok((await db.Products.AsNoTracking().OrderBy(p => p.Code).ToListAsync(ct)).Select(ToResponse).ToList()))
            .RequirePermission(Permissions.Savings.View).WithName("ListSavingsProducts");
        products.MapPost("", async (CreateProductRequest r, SavingsDbContext db, ITenantContext tenant, CancellationToken ct) =>
        {
            var p = SavingsProduct.Create(Ids.New(), tenant.TenantId, r.Code, r.Name, r.Description, r.Kind, r.Segment, r.ControlGlAccountCode, r.AccountSuffix, r.MinimumOpeningDeposit, r.MinimumBalance,
                r.AllowsWithdrawals, r.WithdrawalNoticeDays, r.TellerWithdrawalLimit, r.WithdrawalFee, r.FeeIncomeGlAccountCode, r.InterestRateBps, r.InterestExpenseGlAccountCode, r.TermMonths);
            db.Products.Add(p);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { throw new ConflictException("savings.product.duplicate", $"Product {r.Code} already exists."); }
            return TypedResults.Created($"/api/savings/products/{p.Code}", ToResponse(p));
        }).RequirePermission(Permissions.Savings.ProductsManage).WithName("CreateSavingsProduct");
        products.MapPut("/{code}", async (string code, UpdateProductRequest r, SavingsService savings, SavingsDbContext db, CancellationToken ct) =>
        {
            var p = await savings.GetProductAsync(code, ct);
            p.Update(r.Name, r.Description, r.MinimumOpeningDeposit, r.MinimumBalance, r.WithdrawalNoticeDays, r.TellerWithdrawalLimit, r.WithdrawalFee, r.FeeIncomeGlAccountCode, r.InterestRateBps, r.InterestExpenseGlAccountCode, r.IsActive);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(ToResponse(p));
        }).RequirePermission(Permissions.Savings.ProductsManage).WithName("UpdateSavingsProduct");

        // Public product catalogue for the tenant's marketing site (ADR 0007): active products, public fields only.
        app.MapGet("/api/public/products/savings", async (SavingsDbContext db, CancellationToken ct) =>
            TypedResults.Ok((await db.Products.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Code).ToListAsync(ct))
                .Select(p => new PublicSavingsProduct(p.Code, p.Name, p.Description, p.Kind, p.Segment, p.MinimumOpeningDeposit, p.MinimumBalance, p.AllowsWithdrawals, p.WithdrawalNoticeDays, p.InterestRateBps, p.TermMonths)).ToList()))
            .RequireRateLimiting("public").WithTags("Public").WithName("ListPublicSavingsProducts");

        var accounts = app.MapGroup("/api/savings/accounts").WithTags("Savings accounts");
        accounts.MapPost("", async (OpenAccountRequest r, SavingsService savings, ILedgerService ledger, ICurrentUser user, CancellationToken ct) =>
        {
            var a = await savings.OpenAccountAsync(r.MemberId, r.ProductCode, user.UserId, ct);
            return TypedResults.Created($"/api/savings/accounts/{a.AccountNumber}", await ToResponse(a, ledger, ct));
        }).RequirePermission(Permissions.Savings.AccountsOpen).WithName("OpenSavingsAccount");
        accounts.MapGet("/by-member/{memberId:guid}", async (Guid memberId, SavingsDbContext db, ILedgerService ledger, CancellationToken ct) =>
        {
            var rows = await db.Accounts.AsNoTracking().Where(a => a.MemberId == memberId).OrderBy(a => a.OpenedAt).ToListAsync(ct);
            var result = new List<SavingsAccountResponse>();
            foreach (var a in rows) result.Add(await ToResponse(a, ledger, ct));
            return TypedResults.Ok(result);
        }).RequirePermission(Permissions.Savings.View).WithName("GetMemberSavingsAccounts");
        accounts.MapGet("/{accountNumber}", async Task<Results<Ok<SavingsAccountResponse>, NotFound>> (string accountNumber, SavingsDbContext db, ILedgerService ledger, CancellationToken ct) =>
        {
            var a = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.AccountNumber == accountNumber, ct);
            return a is null ? TypedResults.NotFound() : TypedResults.Ok(await ToResponse(a, ledger, ct));
        }).RequirePermission(Permissions.Savings.View).WithName("GetSavingsAccount");
        accounts.MapPost("/{accountNumber}/deposits", async (string accountNumber, DepositRequest r, SavingsService savings, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(await savings.DepositAsync(new DepositCommand(accountNumber, r.Amount, r.Channel, r.Reference, r.Narrative, user.UserId), ct)))
            .RequirePermission(Permissions.Savings.Deposit).WithName("Deposit");
        accounts.MapPost("/{accountNumber}/withdrawals", async (string accountNumber, WithdrawalRequestDto r, SavingsService savings, ICurrentUser user, CancellationToken ct) =>
        {
            var w = await savings.RequestWithdrawalAsync(accountNumber, r.Amount, r.Channel, r.Destination, r.Narrative, user.UserId, ct);
            return TypedResults.Created($"/api/savings/withdrawals/{w.Id}", ToResponse(w));
        }).RequirePermission(Permissions.Savings.Withdraw).WithName("RequestWithdrawal");

        var withdrawals = app.MapGroup("/api/savings/withdrawals").WithTags("Withdrawals");
        withdrawals.MapGet("", async (SavingsDbContext db, WithdrawalStatus? status, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 200);
            var q = db.Withdrawals.AsNoTracking();
            if (status is WithdrawalStatus s) q = q.Where(w => w.Status == s);
            var total = await q.CountAsync(ct);
            var items = (await q.OrderByDescending(w => w.RequestedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct)).Select(ToResponse).ToList();
            return TypedResults.Ok(new PagedResult<WithdrawalResponse>(items, page, pageSize, total));
        }).RequirePermission(Permissions.Savings.View).WithName("ListWithdrawals");
        withdrawals.MapGet("/{id:guid}", async Task<Results<Ok<WithdrawalResponse>, NotFound>> (Guid id, SavingsDbContext db, CancellationToken ct) =>
        {
            var w = await db.Withdrawals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return w is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(w));
        }).RequirePermission(Permissions.Savings.View).WithName("GetWithdrawal");
        withdrawals.MapPost("/{id:guid}/approve", async (Guid id, SavingsService savings, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await savings.ApproveWithdrawalAsync(id, user.UserId, ct))))
            .RequirePermission(Permissions.Savings.WithdrawalApprove).WithName("ApproveWithdrawal");
        withdrawals.MapPost("/{id:guid}/reject", async (Guid id, ReasonDto r, SavingsService savings, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await savings.RejectWithdrawalAsync(id, r.Reason, user.UserId, ct))))
            .RequirePermission(Permissions.Savings.WithdrawalApprove).WithName("RejectWithdrawal");
        withdrawals.MapPost("/{id:guid}/pay", async (Guid id, SavingsService savings, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await savings.PayWithdrawalAsync(id, user.UserId, ct))))
            .RequirePermission(Permissions.Savings.Withdraw).WithName("PayWithdrawal");

        var fds = app.MapGroup("/api/savings/fixed-deposits").WithTags("Fixed deposits");
        fds.MapPost("", async (OpenFixedDepositRequest r, SavingsService savings, ILedgerService ledger, ICurrentUser user, CancellationToken ct) =>
        {
            var a = await savings.OpenFixedDepositAsync(r.MemberId, r.ProductCode, r.Principal, r.FromAccountNumber, user.UserId, ct);
            return TypedResults.Created($"/api/savings/accounts/{a.AccountNumber}", await ToResponse(a, ledger, ct));
        }).RequirePermission(Permissions.Savings.AccountsOpen).WithName("OpenFixedDeposit");
        fds.MapPost("/{accountNumber}/mature", async (string accountNumber, SavingsService savings, ILedgerService ledger, ICurrentUser user, bool force = false, CancellationToken ct = default) =>
        {
            if (force && env.IsProduction()) throw new ForbiddenException("Forced maturity is a demo/testing affordance and is disabled in production.");
            var a = await savings.MatureFixedDepositAsync(accountNumber, user.UserId, force, ct);
            return TypedResults.Ok(await ToResponse(a, ledger, ct));
        }).RequirePermission(Permissions.Savings.AccountsOpen).WithName("MatureFixedDeposit");

        var dividends = app.MapGroup("/api/savings/dividends").WithTags("Dividends");
        dividends.MapGet("", async (SavingsDbContext db, CancellationToken ct) => TypedResults.Ok((await db.Dividends.AsNoTracking().OrderByDescending(d => d.FinancialYear).ToListAsync(ct)).Select(ToResponse).ToList()))
            .RequirePermission(Permissions.Savings.View).WithName("ListDividends");
        dividends.MapGet("/{id:guid}", async (Guid id, DividendService svc, CancellationToken ct) => TypedResults.Ok(ToResponse(await svc.GetAsync(id, ct))))
            .RequirePermission(Permissions.Savings.View).WithName("GetDividend");
        dividends.MapPost("", async (DeclareDividendRequest r, DividendService svc, ICurrentUser user, CancellationToken ct) =>
        {
            var d = await svc.DeclareAsync(r.FinancialYear, r.ShareRateBps, r.DepositRateBps, user.UserId, ct);
            return TypedResults.Created($"/api/savings/dividends/{d.Id}", ToResponse(d));
        }).RequirePermission(Permissions.Savings.DividendsDeclare).WithName("DeclareDividend");
        dividends.MapPost("/{id:guid}/approve", async (Guid id, DividendService svc, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await svc.ApproveAsync(id, user.UserId, ct))))
            .RequirePermission(Permissions.Savings.DividendsApprove).WithName("ApproveDividend");
        dividends.MapPost("/{id:guid}/reject", async (Guid id, ReasonDto r, DividendService svc, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await svc.RejectAsync(id, r.Reason, user.UserId, ct))))
            .RequirePermission(Permissions.Savings.DividendsApprove).WithName("RejectDividend");
        dividends.MapPost("/{id:guid}/pay", async (Guid id, DividendService svc, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await svc.PayAsync(id, user.UserId, ct))))
            .RequirePermission(Permissions.Savings.DividendsApprove).WithName("PayDividend");
    }

    private static ProductResponse ToResponse(SavingsProduct p) => new(p.Id, p.Code, p.Name, p.Description, p.Kind, p.Segment, p.ControlGlAccountCode, p.AccountSuffix, p.MinimumOpeningDeposit, p.MinimumBalance,
        p.AllowsWithdrawals, p.WithdrawalNoticeDays, p.TellerWithdrawalLimit, p.WithdrawalFee, p.FeeIncomeGlAccountCode, p.InterestRateBps, p.InterestExpenseGlAccountCode, p.TermMonths, p.IsActive);

    private static async Task<SavingsAccountResponse> ToResponse(SavingsAccount a, ILedgerService ledger, CancellationToken ct)
    {
        var l = await ledger.FindAccountAsync(a.AccountNumber, ct);
        return new(a.Id, a.AccountNumber, a.MemberId, a.ProductCode, a.Kind, a.Segment, a.Status, a.OpenedAt, l?.Balance ?? 0, l?.HeldAmount ?? 0, l?.AvailableBalance ?? 0, a.Principal, a.TermMonths, a.InterestRateBps, a.MaturityDate, a.PayoutAccountNumber);
    }

    private static WithdrawalResponse ToResponse(WithdrawalRequest w) => new(w.Id, w.AccountNumber, w.MemberId, w.Amount, w.Fee, w.Channel, w.PayoutDestination, w.Status, w.RequestedByUserId, w.RequestedAt, w.NoticeExpiresOn,
        w.ApprovedByUserId, w.ApprovedAt, w.PaidByUserId, w.PaidAt, w.JournalReference, w.RejectionReason, w.Narrative);

    private static DividendResponse ToResponse(DividendDeclaration d) => new(d.Id, d.FinancialYear, d.ShareDividendRateBps, d.DepositInterestRateBps, d.WithholdingTaxBps, d.Status, d.DeclaredByUserId, d.DeclaredAt, d.ApprovedByUserId, d.ApprovedAt, d.PaidAt,
        d.TotalShareDividend, d.TotalDepositInterest, d.TotalWithholdingTax,
        d.Lines.Select(l => new DividendLineResponse(l.MemberId, l.SharesAccountNumber, l.ShareBalance, l.ShareDividend, l.DepositsAccountNumber, l.DepositBalance, l.DepositInterest, l.WithholdingTax, l.NetPayable, l.PayoutAccountNumber, l.IsPaid)).ToList());
}
