using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Sacco.Modules.Savings.Application;
using Sacco.Modules.Savings.Domain;
using Sacco.Modules.Savings.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Sacco.Shared.Http;
using Sacco.Shared.Ledger;
using Sacco.Shared.Savings;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Savings.Endpoints;

public sealed record ProductResponse(Guid Id, string Code, string Name, string? Description, ProductKind Kind, Segment Segment, string ControlGlAccountCode, string AccountSuffix, decimal MinimumOpeningDeposit, decimal MinimumBalance,
    bool AllowsWithdrawals, int WithdrawalNoticeDays, decimal TellerWithdrawalLimit, decimal WithdrawalFee, string? FeeIncomeGlAccountCode, int InterestRateBps, string? InterestExpenseGlAccountCode, int? TermMonths, bool IsActive,
    ProductListing Listing);
public sealed record CreateProductRequest(string Code, string Name, string? Description, ProductKind Kind, Segment Segment, string ControlGlAccountCode, string AccountSuffix, decimal MinimumOpeningDeposit, decimal MinimumBalance,
    bool AllowsWithdrawals, int WithdrawalNoticeDays, decimal TellerWithdrawalLimit, decimal WithdrawalFee, string? FeeIncomeGlAccountCode, int InterestRateBps, string? InterestExpenseGlAccountCode, int? TermMonths);
public sealed record UpdateProductRequest(string Name, string? Description, decimal MinimumOpeningDeposit, decimal MinimumBalance, int WithdrawalNoticeDays, decimal TellerWithdrawalLimit, decimal WithdrawalFee, string? FeeIncomeGlAccountCode, int InterestRateBps, string? InterestExpenseGlAccountCode, bool IsActive);

public sealed record PublicSavingsProduct(string Code, string Name, string? Description, ProductKind Kind, Segment Segment, decimal MinimumOpeningDeposit, decimal MinimumBalance, bool AllowsWithdrawals, int WithdrawalNoticeDays, int InterestRateBps, int? TermMonths,
    IReadOnlyList<string> Features, IReadOnlyList<string> Requirements, string? AmountNote, string? ApplicationFormUrl, int DisplayOrder);
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
public sealed record SelfWithdrawalRequest(string AccountNumber, decimal Amount, PayoutChannel Channel, string? Destination, string? Narrative);
/// <summary>One year's dividend for the signed-in member — their line from that year's declaration, never anyone else's.</summary>
public sealed record MyDividendResponse(int FinancialYear, DividendStatus Status, decimal ShareDividend, decimal DepositInterest, decimal WithholdingTax, decimal NetPayable, bool IsPaid, DateTimeOffset? PaidAt);
public sealed record DividendLineResponse(Guid MemberId, string? SharesAccountNumber, decimal ShareBalance, decimal ShareDividend, string? DepositsAccountNumber, decimal DepositBalance, decimal DepositInterest, decimal WithholdingTax, decimal NetPayable, string? PayoutAccountNumber, bool IsPaid);
public sealed record DividendResponse(Guid Id, int FinancialYear, int ShareDividendRateBps, int DepositInterestRateBps, int WithholdingTaxBps, DividendStatus Status, Guid DeclaredByUserId, DateTimeOffset DeclaredAt, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAt, DateTimeOffset? PaidAt,
    decimal TotalShareDividend, decimal TotalDepositInterest, decimal TotalWithholdingTax, IReadOnlyList<DividendLineResponse> Lines);

/// <param name="Fee">What this transaction would cost right now.</param>
/// <param name="Basis">How it was priced, e.g. "2% (min 10)", "3 bands", "Product default", "No fee".</param>
public sealed record FeeQuoteResponse(decimal Fee, string Basis, Guid? RuleId);
public sealed record FeeQuoteRequest(FeeTransactionType TransactionType, FeeChannel? Channel, string ProductCode, decimal Amount);
public sealed record FeeRuleResponse(Guid Id, FeeTransactionType TransactionType, FeeChannel? Channel, string? ProductCode, decimal MinAmount, decimal? MaxAmount,
    FeeChargeType ChargeType, decimal? FixedAmount, int? RateBps, decimal? MinCharge, decimal? MaxCharge, IReadOnlyList<FeeTierInput> Tiers, string ChargeDescription,
    string FeeIncomeGlAccountCode, Segment FeeIncomeSegment, FeeRuleStatus Status, Guid? SupersedesRuleId, Guid CreatedByUserId, DateTimeOffset CreatedAt,
    Guid? DecidedByUserId, DateTimeOffset? DecidedAt, string? RejectionReason, DateTimeOffset? DeactivatedAt);

/// <summary>
/// A member's own account. Balances are null while <see cref="BalanceLocked"/> — the product carries a balance-enquiry fee
/// (<see cref="BalanceEnquiryFee"/>) and no paid reveal window is open (ADR 0015).
/// </summary>
public sealed record MySavingsAccountResponse(Guid Id, string AccountNumber, Guid MemberId, string ProductCode, ProductKind Kind, Segment Segment, SavingsAccountStatus Status, DateTimeOffset OpenedAt,
    decimal? Balance, decimal? HeldAmount, decimal? AvailableBalance, decimal? Principal, int? TermMonths, int? InterestRateBps, DateOnly? MaturityDate, string? PayoutAccountNumber,
    bool BalanceLocked, decimal? BalanceEnquiryFee, DateTimeOffset? BalanceVisibleUntil);
/// <summary>Totals for the signed-in member; a bucket is null when any account in it is fee-locked (<see cref="HasLockedBalances"/>).</summary>
public sealed record MySavingsSummaryResponse(Guid MemberId, decimal? BosaDeposits, decimal? Shares, decimal? FosaBalance, decimal? FixedDeposits, int MonthsWithContributions,
    DateOnly? FirstContributionDate, string? FosaAccountNumber, string? BosaDepositAccountNumber, string? SharesAccountNumber, bool HasLockedBalances);
/// <param name="IdempotencyKey">Generated by the app per tap; retrying with the same key never charges twice.</param>
public sealed record RevealBalanceRequest(string IdempotencyKey);
public sealed record RevealBalanceResponse(bool Charged, decimal Fee, DateTimeOffset? VisibleUntil, MySavingsAccountResponse Account);

public sealed record CreateShareListingRequest(decimal Amount);
/// <summary>FOSA account numbers are settlement detail, never exposed to the marketplace UI — only the shares accounts, which the mockup already shows.</summary>
public sealed record ShareListingResponse(Guid Id, Guid SellerMemberId, string SellerSharesAccountNumber, decimal Amount, ShareListingStatus Status, DateTimeOffset ListedAt,
    Guid? BuyerMemberId, string? BuyerSharesAccountNumber, DateTimeOffset? ClaimedAt, Guid? DecidedByUserId, DateTimeOffset? DecidedAt, string? RejectionReason, string? JournalReference);

public sealed class SavingsEndpoints(IHostEnvironment env) : IModuleEndpoints
{
    public void Map(IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/api/savings/products").WithTags("Savings products");
        products.MapGet("", async (SavingsDbContext db, CancellationToken ct) => TypedResults.Ok((await db.Products.AsNoTracking().OrderBy(p => p.Code).ToListAsync(ct)).Select(ToResponse).ToList()))
            .RequirePermission(Permissions.Savings.View).WithName("ListSavingsProducts");
        products.MapPost("", async (CreateProductRequest r, SavingsDbContext db, ITenantContext tenant, IAuditLogger audit, ICurrentUser user, CancellationToken ct) =>
        {
            var p = SavingsProduct.Create(Ids.New(), tenant.TenantId, r.Code, r.Name, r.Description, r.Kind, r.Segment, r.ControlGlAccountCode, r.AccountSuffix, r.MinimumOpeningDeposit, r.MinimumBalance,
                r.AllowsWithdrawals, r.WithdrawalNoticeDays, r.TellerWithdrawalLimit, r.WithdrawalFee, r.FeeIncomeGlAccountCode, r.InterestRateBps, r.InterestExpenseGlAccountCode, r.TermMonths);
            db.Products.Add(p);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { throw new ConflictException("savings.product.duplicate", $"Product {r.Code} already exists."); }
            await audit.RecordAsync(new AuditEvent("savings.product.created", nameof(SavingsProduct), p.Code, user.UserId,
                AuditDetails.New().With("name", p.Name).With("kind", p.Kind.ToString()).With("segment", p.Segment.ToString()).With("interestBps", p.InterestRateBps).ToJson()), ct);
            return TypedResults.Created($"/api/savings/products/{p.Code}", ToResponse(p));
        }).RequirePermission(Permissions.Savings.ProductsManage).WithName("CreateSavingsProduct");
        products.MapPut("/{code}", async (string code, UpdateProductRequest r, SavingsService savings, SavingsDbContext db, IAuditLogger audit, ICurrentUser user, CancellationToken ct) =>
        {
            var p = await savings.GetProductAsync(code, ct);
            var before = AuditDetails.New().With("name", p.Name).With("minimumOpeningDeposit", p.MinimumOpeningDeposit).With("interestBps", p.InterestRateBps).With("withdrawalFee", p.WithdrawalFee).With("active", p.IsActive);
            p.Update(r.Name, r.Description, r.MinimumOpeningDeposit, r.MinimumBalance, r.WithdrawalNoticeDays, r.TellerWithdrawalLimit, r.WithdrawalFee, r.FeeIncomeGlAccountCode, r.InterestRateBps, r.InterestExpenseGlAccountCode, r.IsActive);
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new AuditEvent("savings.product.updated", nameof(SavingsProduct), p.Code, user.UserId,
                AuditDetails.New().With("before", System.Text.Json.JsonDocument.Parse(before.ToString()).RootElement)
                    .With("after", new { name = p.Name, minimumOpeningDeposit = p.MinimumOpeningDeposit, interestBps = p.InterestRateBps, withdrawalFee = p.WithdrawalFee, active = p.IsActive }).ToJson()), ct);
            return TypedResults.Ok(ToResponse(p));
        }).RequirePermission(Permissions.Savings.ProductsManage).WithName("UpdateSavingsProduct");
        // Public-website presentation only; never changes how the product behaves.
        products.MapPut("/{code}/listing", async (string code, ProductListing r, SavingsService savings, SavingsDbContext db, IAuditLogger audit, ICurrentUser user, CancellationToken ct) =>
        {
            var p = await savings.GetProductAsync(code, ct);
            var wasShown = p.Listing.ShowOnPublicSite;
            p.SetListing(r.ToDomain());
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new AuditEvent("savings.product.listing_changed", nameof(SavingsProduct), p.Code, user.UserId,
                AuditDetails.New().Changed("shownOnPublicSite", wasShown, p.Listing.ShowOnPublicSite).With("displayOrder", p.Listing.DisplayOrder).ToJson()), ct);
            return TypedResults.Ok(ToResponse(p));
        }).RequirePermission(Permissions.Savings.ProductsManage).WithName("UpdateSavingsProductListing");

        // Public product catalogue for the tenant's marketing site (ADR 0007): active products, public fields only.
        app.MapGet("/api/public/products/savings", async (SavingsDbContext db, CancellationToken ct) =>
            TypedResults.Ok((await db.Products.AsNoTracking().Where(p => p.IsActive && p.Listing.ShowOnPublicSite).OrderBy(p => p.Listing.DisplayOrder).ThenBy(p => p.Name).ToListAsync(ct))
                .Select(p => new PublicSavingsProduct(p.Code, p.Name, p.Description, p.Kind, p.Segment, p.MinimumOpeningDeposit, p.MinimumBalance, p.AllowsWithdrawals, p.WithdrawalNoticeDays, p.InterestRateBps, p.TermMonths,
                    p.Listing.Features, p.Listing.Requirements, p.Listing.AmountNote, p.Listing.ApplicationFormUrl, p.Listing.DisplayOrder)).ToList()))
            .RequireRateLimiting("public-read").WithTags("Public").WithName("ListPublicSavingsProducts");

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

        // ---- Member self-service ----
        var self = app.MapGroup("/api/self").WithTags("Self-service");
        self.MapGet("/accounts", async (SavingsDbContext db, ILedgerService ledger, BalanceEnquiryService enquiries, ICurrentUser user, CancellationToken ct) =>
        {
            var memberId = user.RequireMemberId();
            var rows = await db.Accounts.AsNoTracking().Where(a => a.MemberId == memberId).OrderBy(a => a.OpenedAt).ToListAsync(ct);
            var visibility = await enquiries.VisibilityAsync(memberId, rows, ct);
            var result = new List<MySavingsAccountResponse>();
            foreach (var a in rows) result.Add(await ToMyResponse(a, visibility[a.AccountNumber], ledger, ct));
            return TypedResults.Ok(result);
        }).RequirePermission(Permissions.Self.AccountsView).WithName("GetMyAccounts");
        self.MapGet("/summary", async (SavingsService savings, SavingsDbContext db, BalanceEnquiryService enquiries, ICurrentUser user, CancellationToken ct) =>
        {
            var memberId = user.RequireMemberId();
            var s = await savings.GetMemberSummaryAsync(memberId, ct);
            var rows = await db.Accounts.AsNoTracking().Where(a => a.MemberId == memberId && a.Status == SavingsAccountStatus.Active).ToListAsync(ct);
            var visibility = await enquiries.VisibilityAsync(memberId, rows, ct);
            var locked = rows.Where(a => visibility[a.AccountNumber].Locked).Select(a => a.Kind).ToHashSet();
            decimal? Hide(ProductKind kind, decimal value) => locked.Contains(kind) ? null : value;
            return TypedResults.Ok(new MySavingsSummaryResponse(s.MemberId, Hide(ProductKind.BosaDeposit, s.BosaDeposits), Hide(ProductKind.Shares, s.Shares), Hide(ProductKind.FosaCurrent, s.FosaBalance),
                Hide(ProductKind.FixedDeposit, s.FixedDeposits), s.MonthsWithContributions, s.FirstContributionDate, s.FosaAccountNumber, s.BosaDepositAccountNumber, s.SharesAccountNumber, locked.Count > 0));
        }).RequirePermission(Permissions.Self.AccountsView).WithName("GetMySavingsSummary");

        // ---- Member self-service: pay to see a fee-gated balance (ADR 0015). ----
        self.MapPost("/accounts/{accountNumber}/balance-enquiries", async (string accountNumber, RevealBalanceRequest r, SavingsDbContext db, ILedgerService ledger, BalanceEnquiryService enquiries, ICurrentUser user, CancellationToken ct) =>
        {
            var memberId = user.RequireMemberId();
            var result = await enquiries.RevealAsync(memberId, accountNumber, r.IdempotencyKey, user.UserId, ct);
            var account = await db.Accounts.AsNoTracking().FirstAsync(a => a.AccountNumber == accountNumber, ct);
            var visibility = await enquiries.VisibilityAsync(memberId, [account], ct);
            return TypedResults.Ok(new RevealBalanceResponse(result.Charged, result.Fee, result.VisibleUntil, await ToMyResponse(account, visibility[accountNumber], ledger, ct)));
        }).RequirePermission(Permissions.Self.AccountsView).WithName("RevealMyBalance");

        // ---- Member self-service: what a deposit/withdrawal/enquiry would cost before committing to it. ----
        self.MapGet("/fees/quote", async (FeeTransactionType transactionType, string accountNumber, FeeChannel? channel, decimal? amount, SavingsDbContext db, FeeMatrixService fees, ICurrentUser user, CancellationToken ct) =>
        {
            var memberId = user.RequireMemberId();
            var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountNumber == accountNumber && a.MemberId == memberId, ct)
                          ?? throw new NotFoundException("Account", accountNumber);
            var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == account.ProductId, ct);
            var fee = await fees.QuoteAsync(transactionType, channel, product, amount ?? 0m, ct);
            return TypedResults.Ok(new FeeQuoteResponse(fee.Amount, fee.Basis, fee.RuleId));
        }).RequirePermission(Permissions.Self.AccountsView).WithName("QuoteMyFee");

        // ---- Member self-service: request a withdrawal from my own account. Still maker-checker (ADR 0014) —
        // this only creates the PendingApproval request; a staff member approves and pays it exactly as today. ----
        self.MapPost("/withdrawals", async (SelfWithdrawalRequest r, SavingsDbContext db, SavingsService savings, ICurrentUser user, CancellationToken ct) =>
        {
            var memberId = user.RequireMemberId();
            var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountNumber == r.AccountNumber, ct);
            if (account is null || account.MemberId != memberId) throw new NotFoundException("Account", r.AccountNumber);
            var w = await savings.RequestWithdrawalAsync(r.AccountNumber, r.Amount, r.Channel, r.Destination, r.Narrative, user.UserId, ct);
            return TypedResults.Created($"/api/self/withdrawals/{w.Id}", ToResponse(w));
        }).RequirePermission(Permissions.Self.WithdrawalsRequest).WithName("RequestMyWithdrawal");
        self.MapGet("/withdrawals", async (SavingsDbContext db, ICurrentUser user, CancellationToken ct) =>
        {
            var memberId = user.RequireMemberId();
            return TypedResults.Ok((await db.Withdrawals.AsNoTracking().Where(w => w.MemberId == memberId).OrderByDescending(w => w.RequestedAt).ToListAsync(ct)).Select(ToResponse).ToList());
        }).RequirePermission(Permissions.Self.WithdrawalsRequest).WithName("GetMyWithdrawals");

        // ---- Member self-service: dividends earned by financial year (never another member's line). ----
        self.MapGet("/dividends", async (SavingsDbContext db, ICurrentUser user, int? year, CancellationToken ct) =>
        {
            var memberId = user.RequireMemberId();
            var declarations = await db.Dividends.AsNoTracking()
                .Where(d => d.Lines.Any(l => l.MemberId == memberId) && (year == null || d.FinancialYear == year))
                .OrderByDescending(d => d.FinancialYear).ToListAsync(ct);
            var result = declarations.Select(d =>
            {
                var line = d.Lines.First(l => l.MemberId == memberId);
                return new MyDividendResponse(d.FinancialYear, d.Status, line.ShareDividend, line.DepositInterest, line.WithholdingTax, line.NetPayable, line.IsPaid, line.IsPaid ? d.PaidAt : null);
            }).ToList();
            return TypedResults.Ok(result);
        }).RequirePermission(Permissions.Self.DividendsView).WithName("GetMyDividends");

        // ---- Member self-service: shares marketplace (ADR 0008 follow-up) — shares aren't withdrawable,
        // only sold to another member. Still maker-checker: a staff member must approve before anything moves. ----
        self.MapPost("/shares-marketplace/listings", async (CreateShareListingRequest r, ShareMarketplaceService marketplace, ICurrentUser user, CancellationToken ct) =>
        {
            var listing = await marketplace.ListAsync(user.RequireMemberId(), r.Amount, user.UserId, ct);
            return TypedResults.Created($"/api/self/shares-marketplace/listings/{listing.Id}", ToResponse(listing));
        }).RequirePermission(Permissions.Self.SharesMarketplaceTrade).WithName("ListMySharesForSale");
        self.MapGet("/shares-marketplace/listings/open", async (ShareMarketplaceService marketplace, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok((await marketplace.GetOpenListingsAsync(user.RequireMemberId(), ct)).Select(ToResponse).ToList()))
            .RequirePermission(Permissions.Self.SharesMarketplaceTrade).WithName("BrowseOpenShareListings");
        self.MapGet("/shares-marketplace/listings/mine", async (ShareMarketplaceService marketplace, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok((await marketplace.GetMyListingsAsync(user.RequireMemberId(), ct)).Select(ToResponse).ToList()))
            .RequirePermission(Permissions.Self.SharesMarketplaceTrade).WithName("GetMyShareListings");
        self.MapPost("/shares-marketplace/listings/{id:guid}/claim", async (Guid id, ShareMarketplaceService marketplace, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await marketplace.ClaimAsync(id, user.RequireMemberId(), user.UserId, ct))))
            .RequirePermission(Permissions.Self.SharesMarketplaceTrade).WithName("ClaimShareListing");
        self.MapPost("/shares-marketplace/listings/{id:guid}/cancel", async (Guid id, ShareMarketplaceService marketplace, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await marketplace.CancelAsync(id, user.RequireMemberId(), ct))))
            .RequirePermission(Permissions.Self.SharesMarketplaceTrade).WithName("CancelMyShareListing");

        var feeRules = app.MapGroup("/api/savings/fees").WithTags("Fee matrix");
        feeRules.MapGet("", async (FeeMatrixService fees, FeeRuleStatus? status, FeeTransactionType? transactionType, CancellationToken ct) =>
            TypedResults.Ok((await fees.ListAsync(status, transactionType, ct)).Select(ToResponse).ToList()))
            .RequirePermission(Permissions.Savings.View).WithName("ListFeeRules");
        feeRules.MapGet("/{id:guid}", async (Guid id, FeeMatrixService fees, CancellationToken ct) => TypedResults.Ok(ToResponse(await fees.GetAsync(id, ct))))
            .RequirePermission(Permissions.Savings.View).WithName("GetFeeRule");
        feeRules.MapPost("", async (CreateFeeRuleCommand r, FeeMatrixService fees, ICurrentUser user, CancellationToken ct) =>
        {
            var rule = await fees.CreateAsync(r, user.UserId, ct);
            return TypedResults.Created($"/api/savings/fees/{rule.Id}", ToResponse(rule));
        }).RequirePermission(Permissions.Savings.FeesManage).WithName("CreateFeeRule");
        feeRules.MapPost("/{id:guid}/approve", async (Guid id, FeeMatrixService fees, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await fees.ApproveAsync(id, user.UserId, ct))))
            .RequirePermission(Permissions.Savings.FeesApprove).WithName("ApproveFeeRule");
        feeRules.MapPost("/{id:guid}/reject", async (Guid id, ReasonDto r, FeeMatrixService fees, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await fees.RejectAsync(id, r.Reason, user.UserId, ct))))
            .RequirePermission(Permissions.Savings.FeesApprove).WithName("RejectFeeRule");
        feeRules.MapPost("/{id:guid}/deactivate", async (Guid id, FeeMatrixService fees, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await fees.DeactivateAsync(id, user.UserId, ct))))
            .RequirePermission(Permissions.Savings.FeesApprove).WithName("DeactivateFeeRule");
        feeRules.MapPost("/quote", async (FeeQuoteRequest r, SavingsService savings, FeeMatrixService fees, CancellationToken ct) =>
        {
            var fee = await fees.QuoteAsync(r.TransactionType, r.Channel, await savings.GetProductAsync(r.ProductCode, ct), r.Amount, ct);
            return TypedResults.Ok(new FeeQuoteResponse(fee.Amount, fee.Basis, fee.RuleId));
        }).RequirePermission(Permissions.Savings.View).WithName("QuoteFee");

        var sharesMarketplace = app.MapGroup("/api/savings/shares-marketplace").WithTags("Shares marketplace");
        sharesMarketplace.MapGet("", async (ShareMarketplaceService marketplace, ShareListingStatus? status, CancellationToken ct) =>
            TypedResults.Ok((await marketplace.ListAsync(status, ct)).Select(ToResponse).ToList()))
            .RequirePermission(Permissions.Savings.View).WithName("ListShareListings");
        sharesMarketplace.MapGet("/pending", async (ShareMarketplaceService marketplace, CancellationToken ct) =>
            TypedResults.Ok((await marketplace.GetPendingApprovalAsync(ct)).Select(ToResponse).ToList()))
            .RequirePermission(Permissions.Savings.View).WithName("ListPendingShareListings");
        sharesMarketplace.MapGet("/{id:guid}", async (Guid id, ShareMarketplaceService marketplace, CancellationToken ct) => TypedResults.Ok(ToResponse(await marketplace.GetAsync(id, ct))))
            .RequirePermission(Permissions.Savings.View).WithName("GetShareListing");
        sharesMarketplace.MapPost("/{id:guid}/approve", async (Guid id, ShareMarketplaceService marketplace, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await marketplace.ApproveAsync(id, user.UserId, ct))))
            .RequirePermission(Permissions.Savings.SharesMarketplaceApprove).WithName("ApproveShareListing");
        sharesMarketplace.MapPost("/{id:guid}/reject", async (Guid id, ReasonDto r, ShareMarketplaceService marketplace, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await marketplace.RejectAsync(id, r.Reason, user.UserId, ct))))
            .RequirePermission(Permissions.Savings.SharesMarketplaceApprove).WithName("RejectShareListing");

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
        p.AllowsWithdrawals, p.WithdrawalNoticeDays, p.TellerWithdrawalLimit, p.WithdrawalFee, p.FeeIncomeGlAccountCode, p.InterestRateBps, p.InterestExpenseGlAccountCode, p.TermMonths, p.IsActive,
        ProductListing.From(p.Listing));

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

    private static async Task<MySavingsAccountResponse> ToMyResponse(SavingsAccount a, BalanceVisibility v, ILedgerService ledger, CancellationToken ct)
    {
        var l = v.Locked ? null : await ledger.FindAccountAsync(a.AccountNumber, ct);
        return new(a.Id, a.AccountNumber, a.MemberId, a.ProductCode, a.Kind, a.Segment, a.Status, a.OpenedAt,
            v.Locked ? null : l?.Balance ?? 0, v.Locked ? null : l?.HeldAmount ?? 0, v.Locked ? null : l?.AvailableBalance ?? 0,
            a.Principal, a.TermMonths, a.InterestRateBps, a.MaturityDate, a.PayoutAccountNumber, v.Locked, v.Fee, v.VisibleUntil);
    }

    private static FeeRuleResponse ToResponse(FeeRule r) => new(r.Id, r.TransactionType, r.Channel, r.ProductCode, r.MinAmount, r.MaxAmount, r.ChargeType, r.FixedAmount, r.RateBps,
        r.MinCharge, r.MaxCharge, r.Tiers.OrderBy(t => t.UpTo ?? decimal.MaxValue).Select(t => new FeeTierInput(t.UpTo, t.Charge)).ToList(), r.Describe(), r.FeeIncomeGlAccountCode, r.FeeIncomeSegment,
        r.Status, r.SupersedesRuleId, r.CreatedByUserId, r.CreatedAt, r.DecidedByUserId, r.DecidedAt, r.RejectionReason, r.DeactivatedAt);

    private static ShareListingResponse ToResponse(ShareListing l) => new(l.Id, l.SellerMemberId, l.SellerSharesAccountNumber, l.Amount, l.Status, l.ListedAt,
        l.BuyerMemberId, l.BuyerSharesAccountNumber, l.ClaimedAt, l.DecidedByUserId, l.DecidedAt, l.RejectionReason, l.JournalReference);
}
