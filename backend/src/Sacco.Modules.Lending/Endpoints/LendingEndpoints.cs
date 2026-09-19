using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sacco.Modules.Lending.Application;
using Sacco.Modules.Lending.Domain;
using Sacco.Modules.Lending.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Sacco.Shared.Http;
using Sacco.Shared.Ledger;
using Sacco.Shared.Lending;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Lending.Endpoints;

public sealed record LoanProductResponse(Guid Id, string Code, string Name, string? Description, Segment Segment, string ControlGlAccountCode, string InterestIncomeGlAccountCode, string InterestReceivableGlAccountCode, string FeeIncomeGlAccountCode, string ProvisionGlAccountCode, string ProvisionExpenseGlAccountCode,
    int InterestRateBps, InterestMethod InterestMethod, decimal MinAmount, decimal MaxAmount, int MinTermMonths, int MaxTermMonths, decimal DepositMultiplier, int MinMembershipMonths, int ProcessingFeeBps, bool RequiresGuarantors, int MinGuarantors, decimal CommitteeThreshold, int CommitteeApprovalsRequired, int GracePeriodDays, bool IsActive,
    LoanCategory Category, ProductListing Listing);
public sealed record CreateLoanProductRequest(string Code, string Name, string? Description, Segment Segment, string ControlGlAccountCode, string InterestIncomeGlAccountCode, string InterestReceivableGlAccountCode, string FeeIncomeGlAccountCode, string ProvisionGlAccountCode, string ProvisionExpenseGlAccountCode,
    int InterestRateBps, InterestMethod InterestMethod, decimal MinAmount, decimal MaxAmount, int MinTermMonths, int MaxTermMonths, decimal DepositMultiplier, int MinMembershipMonths, int ProcessingFeeBps, bool RequiresGuarantors, int MinGuarantors, decimal CommitteeThreshold, int CommitteeApprovalsRequired, int GracePeriodDays);
public sealed record UpdateLoanProductRequest(string Name, string? Description, int InterestRateBps, decimal MinAmount, decimal MaxAmount, int MinTermMonths, int MaxTermMonths, decimal DepositMultiplier, int MinMembershipMonths, int ProcessingFeeBps, bool RequiresGuarantors, int MinGuarantors, decimal CommitteeThreshold, int CommitteeApprovalsRequired, int GracePeriodDays, bool IsActive);

public sealed record PublicLoanProduct(string Code, string Name, string? Description, Segment Segment, int InterestRateBps, InterestMethod InterestMethod, decimal MinAmount, decimal MaxAmount, int MinTermMonths, int MaxTermMonths, decimal DepositMultiplier, int MinMembershipMonths, bool RequiresGuarantors, int MinGuarantors,
    LoanCategory Category, IReadOnlyList<string> Features, IReadOnlyList<string> Requirements, string? AmountNote, string? ApplicationFormUrl, int DisplayOrder);
/// <summary>The public-website presentation of a loan product: which group it's listed under, plus its listing.</summary>
public sealed record LoanProductListingRequest(LoanCategory Category, ProductListing Listing);
public sealed record ApplyLoanRequest(Guid MemberId, string ProductCode, decimal Amount, int TermMonths, string Purpose, string? DisbursementAccountNumber, bool BureauConsent = false);
public sealed record SelfApplyLoanRequest(string ProductCode, decimal Amount, int TermMonths, string Purpose, bool BureauConsent);
public sealed record RestructureRequest(int NewTermMonths, int? NewInterestRateBps, string Reason);
public sealed record LoanAdjustmentResponse(Guid Id, Guid LoanId, string LoanNumber, LoanAdjustmentKind Kind, LoanAdjustmentStatus Status, string Reason, Guid RequestedByUserId, DateTimeOffset RequestedAt,
    Guid? DecidedByUserId, DateTimeOffset? DecidedAt, string? DecisionNotes, int? NewTermMonths, int? NewInterestRateBps, decimal? PrincipalWrittenOff, decimal? InterestReversed, Guid? JournalEntryId);
public sealed record BureauConsentInfo(bool Required, string Text);
public sealed record AddGuarantorRequest(Guid GuarantorMemberId, decimal Amount);
public sealed record NotesRequest(string? Notes);
public sealed record ReasonRequest(string Reason);
public sealed record RepayRequest(decimal Amount, RepaymentChannel Channel, string Reference, string? Narrative);
public sealed record EligibilityResponse(decimal BosaDeposits, decimal Shares, decimal DepositMultiplier, decimal MaxEligibleAmount, int MembershipMonths, int ContributionMonths, decimal ExistingOutstanding);
public sealed record GuarantorResponse(Guid Id, Guid GuarantorMemberId, string DepositsAccountNumber, decimal AmountGuaranteed, GuarantorStatus Status, DateTimeOffset AddedAt, DateTimeOffset? AcceptedAt);
public sealed record ApprovalResponse(Guid ApproverUserId, ApprovalDecision Decision, string? Notes, DateTimeOffset DecidedAt);
public sealed record InstallmentResponse(int Number, DateOnly DueDate, decimal PrincipalDue, decimal InterestDue, decimal PrincipalPaid, decimal InterestPaid, decimal Outstanding, bool InterestAccrued, InstallmentStatus Status);
public sealed record LoanResponse(Guid Id, string LoanNumber, Guid MemberId, string ProductCode, Segment Segment, decimal Amount, int TermMonths, int InterestRateBps, InterestMethod InterestMethod, string Purpose, LoanStatus Status,
    EligibilityResponse Eligibility, string DisbursementAccountNumber, string? PledgedDepositsAccountNumber, decimal PledgedDepositsAmount, string? LedgerAccountNumber, decimal OutstandingPrincipal, decimal ArrearsAmount, int DaysInArrears,
    DateTimeOffset AppliedAt, Guid AppliedByUserId, Guid? AppraisedByUserId, string? AppraisalNotes, DateTimeOffset? ApprovedAt, DateOnly? DisbursementDate, Guid? DisbursedByUserId, decimal ProcessingFee, string? RejectionReason, int ApprovalsRequired,
    IReadOnlyList<GuarantorResponse> Guarantors, IReadOnlyList<ApprovalResponse> Approvals, IReadOnlyList<InstallmentResponse> Schedule, CreditScoreSummary? CreditScore,
    DateTimeOffset? BureauConsentAt, int RestructureCount, IReadOnlyList<LoanAdjustmentResponse> Adjustments);
public sealed record LoanListItem(Guid Id, string LoanNumber, Guid MemberId, string ProductCode, decimal Amount, LoanStatus Status, DateTimeOffset AppliedAt, DateOnly? DisbursementDate, CreditScoreSummary? CreditScore);
public sealed record CreditScoreSummary(int Score, string Grade, ScoreRecommendation Recommendation, BureauStatus BureauStatus, ScoreStage Stage, DateTimeOffset ComputedAt);
public sealed record CreditScoreResponse(Guid Id, Guid LoanId, ScoreStage Stage, int Score, string Grade, ScoreRecommendation Recommendation, string RecommendationReason, BureauStatus BureauStatus, string? BureauReference, string? BureauNarrative,
    IReadOnlyList<FactorScore> Factors, DateTimeOffset ScorecardUpdatedAt, DateTimeOffset ComputedAt, Guid ComputedByUserId);
public sealed record ScorecardResponse(IReadOnlyList<ScorecardFactorDraft> Factors, int TotalPoints, int ApproveThreshold, int ReferThreshold, bool DeclineIfBureauListed, string? Source, DateTimeOffset UpdatedAt, Guid UpdatedByUserId);
public sealed record SetScorecardRequest(IReadOnlyList<ScorecardFactorDraft> Factors, int ApproveThreshold, int ReferThreshold, bool DeclineIfBureauListed, string? Source);
public sealed record ProvisioningConfigResponse(IReadOnlyList<AgingBucketDraft> Buckets, string? Source, DateTimeOffset UpdatedAt, Guid UpdatedByUserId);
public sealed record SetProvisioningConfigRequest(IReadOnlyList<AgingBucketDraft> Buckets, string? Source);
public sealed record ProvisioningLineResponse(string LoanNumber, Guid MemberId, string ProductCode, Segment Segment, int DaysInArrears, string Bucket, int ProvisionRateBps, decimal OutstandingPrincipal, decimal ArrearsAmount, decimal ProvisionRequired);
public sealed record ProvisioningRunResponse(Guid Id, DateOnly AsOf, ProvisioningRunStatus Status, Guid ComputedByUserId, DateTimeOffset ComputedAt, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAt, string? RejectionReason, decimal TotalOutstanding, decimal TotalProvisionRequired, IReadOnlyList<ProvisioningLineResponse> Lines);

public sealed class LendingEndpoints(IHostEnvironment env) : IModuleEndpoints
{
    public void Map(IEndpointRouteBuilder app)
    {
        var products = app.MapGroup("/api/loans/products").WithTags("Loan products");
        products.MapGet("", async (LendingDbContext db, CancellationToken ct) => TypedResults.Ok((await db.Products.AsNoTracking().OrderBy(p => p.Code).ToListAsync(ct)).Select(ToResponse).ToList()))
            .RequirePermission(Permissions.Loans.View).WithName("ListLoanProducts");
        products.MapPost("", async (CreateLoanProductRequest r, LendingDbContext db, ITenantContext tenant, IAuditLogger audit, ICurrentUser user, CancellationToken ct) =>
        {
            var p = LoanProduct.Create(Ids.New(), tenant.TenantId, r.Code, r.Name, r.Description, r.Segment, r.ControlGlAccountCode, r.InterestIncomeGlAccountCode, r.InterestReceivableGlAccountCode, r.FeeIncomeGlAccountCode, r.ProvisionGlAccountCode, r.ProvisionExpenseGlAccountCode,
                r.InterestRateBps, r.InterestMethod, r.MinAmount, r.MaxAmount, r.MinTermMonths, r.MaxTermMonths, r.DepositMultiplier, r.MinMembershipMonths, r.ProcessingFeeBps, r.RequiresGuarantors, r.MinGuarantors, r.CommitteeThreshold, r.CommitteeApprovalsRequired, r.GracePeriodDays);
            db.Products.Add(p);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { throw new ConflictException("loans.product.duplicate", $"Product {r.Code} already exists."); }
            await audit.RecordAsync(new AuditEvent("loans.product.created", nameof(LoanProduct), p.Code, user.UserId,
                AuditDetails.New().With("name", p.Name).With("segment", p.Segment.ToString()).With("interestBps", p.InterestRateBps).With("method", p.InterestMethod.ToString()).ToJson()), ct);
            return TypedResults.Created($"/api/loans/products/{p.Code}", ToResponse(p));
        }).RequirePermission(Permissions.Loans.ProductsManage).WithName("CreateLoanProduct");
        products.MapPut("/{code}", async (string code, UpdateLoanProductRequest r, LoanService loans, LendingDbContext db, IAuditLogger audit, ICurrentUser user, CancellationToken ct) =>
        {
            var p = await loans.GetProductAsync(code, ct);
            var before = new { rateBps = p.InterestRateBps, maxAmount = p.MaxAmount, maxTermMonths = p.MaxTermMonths, multiplier = p.DepositMultiplier, active = p.IsActive };
            p.Update(r.Name, r.Description, r.InterestRateBps, r.MinAmount, r.MaxAmount, r.MinTermMonths, r.MaxTermMonths, r.DepositMultiplier, r.MinMembershipMonths, r.ProcessingFeeBps, r.RequiresGuarantors, r.MinGuarantors, r.CommitteeThreshold, r.CommitteeApprovalsRequired, r.GracePeriodDays, r.IsActive);
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new AuditEvent("loans.product.updated", nameof(LoanProduct), p.Code, user.UserId,
                AuditDetails.New().With("before", before)
                    .With("after", new { rateBps = p.InterestRateBps, maxAmount = p.MaxAmount, maxTermMonths = p.MaxTermMonths, multiplier = p.DepositMultiplier, active = p.IsActive }).ToJson()), ct);
            return TypedResults.Ok(ToResponse(p));
        }).RequirePermission(Permissions.Loans.ProductsManage).WithName("UpdateLoanProduct");
        // Public-website presentation only; never changes eligibility, pricing or approval rules.
        products.MapPut("/{code}/listing", async (string code, LoanProductListingRequest r, LoanService loans, LendingDbContext db, IAuditLogger audit, ICurrentUser user, CancellationToken ct) =>
        {
            var p = await loans.GetProductAsync(code, ct);
            var (wasShown, wasCategory) = (p.Listing.ShowOnPublicSite, p.Category);
            p.SetListing(r.Category, r.Listing.ToDomain());
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new AuditEvent("loans.product.listing_changed", nameof(LoanProduct), p.Code, user.UserId,
                AuditDetails.New().Changed("category", wasCategory.ToString(), p.Category.ToString()).Changed("shownOnPublicSite", wasShown, p.Listing.ShowOnPublicSite).ToJson()), ct);
            return TypedResults.Ok(ToResponse(p));
        }).RequirePermission(Permissions.Loans.ProductsManage).WithName("UpdateLoanProductListing");

        app.MapGet("/api/public/products/loans", async (LendingDbContext db, CancellationToken ct) =>
            TypedResults.Ok((await db.Products.AsNoTracking().Where(p => p.IsActive && p.Listing.ShowOnPublicSite).OrderBy(p => p.Category).ThenBy(p => p.Listing.DisplayOrder).ThenBy(p => p.Name).ToListAsync(ct))
                .Select(p => new PublicLoanProduct(p.Code, p.Name, p.Description, p.Segment, p.InterestRateBps, p.InterestMethod, p.MinAmount, p.MaxAmount, p.MinTermMonths, p.MaxTermMonths, p.DepositMultiplier, p.MinMembershipMonths, p.RequiresGuarantors, p.MinGuarantors,
                    p.Category, p.Listing.Features, p.Listing.Requirements, p.Listing.AmountNote, p.Listing.ApplicationFormUrl, p.Listing.DisplayOrder)).ToList()))
            .RequireRateLimiting("public-read").WithTags("Public").WithName("ListPublicLoanProducts");

        var g = app.MapGroup("/api/loans").WithTags("Loans");
        g.MapGet("", async (LendingDbContext db, CreditScoringService scoring, LoanStatus? status, Guid? memberId, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 200);
            var q = db.Loans.AsNoTracking();
            if (status is LoanStatus s) q = q.Where(l => l.Status == s);
            if (memberId is Guid m) q = q.Where(l => l.MemberId == m);
            var total = await q.CountAsync(ct);
            var loans = await q.OrderByDescending(l => l.AppliedAt).Skip((page - 1) * pageSize).Take(pageSize)
                .Select(l => new { l.Id, l.LoanNumber, l.MemberId, l.ProductCode, l.Amount, l.Status, l.AppliedAt, l.DisbursementDate }).ToListAsync(ct);
            var scores = await scoring.LatestForAsync(loans.Select(l => l.Id).ToList(), ct);
            var items = loans.Select(l => new LoanListItem(l.Id, l.LoanNumber, l.MemberId, l.ProductCode, l.Amount, l.Status, l.AppliedAt, l.DisbursementDate, scores.TryGetValue(l.Id, out var sc) ? Summary(sc) : null)).ToList();
            return TypedResults.Ok(new PagedResult<LoanListItem>(items, page, pageSize, total));
        }).RequirePermission(Permissions.Loans.View).WithName("ListLoans");
        g.MapGet("/eligibility", async (Guid memberId, string productCode, LoanService loans, CancellationToken ct) =>
        {
            var product = await loans.GetProductAsync(productCode, ct);
            return TypedResults.Ok(ToResponse(await loans.EvaluateEligibilityAsync(memberId, product, ct)));
        }).RequirePermission(Permissions.Loans.View).WithName("GetLoanEligibility");
        g.MapGet("/guarantors/{memberId:guid}/exposure", async (Guid memberId, LoanService loans, CancellationToken ct) => TypedResults.Ok(await loans.GetGuarantorExposureAsync(memberId, ct)))
            .RequirePermission(Permissions.Loans.View).WithName("GetGuarantorExposure");
        g.MapGet("/{id:guid}", async Task<Results<Ok<LoanResponse>, NotFound>> (Guid id, LendingDbContext db, CreditScoringService scoring, LoanAdjustmentService adjustments, ILedgerService ledger, IClock clock, CancellationToken ct) =>
        {
            var l = await db.Loans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (l is null) return TypedResults.NotFound();
            var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == l.ProductId, ct);
            return TypedResults.Ok(await ToResponse(l, product, ledger, clock, ct, await scoring.GetLatestAsync(id, ct), await adjustments.ForLoanAsync(id, ct)));
        }).RequirePermission(Permissions.Loans.View).WithName("GetLoan");

        // ---- Write-off and restructuring: maker-checker, approval posts the GL / rebuilds the schedule ----
        g.MapPost("/{id:guid}/write-off", async (Guid id, ReasonRequest r, LoanAdjustmentService svc, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await svc.RequestWriteOffAsync(id, r.Reason, user.UserId, ct))))
            .RequirePermission(Permissions.Loans.Restructure).WithName("RequestLoanWriteOff");
        g.MapPost("/{id:guid}/restructure", async (Guid id, RestructureRequest r, LoanAdjustmentService svc, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await svc.RequestRestructureAsync(id, r.NewTermMonths, r.NewInterestRateBps, r.Reason, user.UserId, ct))))
            .RequirePermission(Permissions.Loans.Restructure).WithName("RequestLoanRestructure");
        g.MapGet("/{id:guid}/adjustments", async (Guid id, LoanAdjustmentService svc, CancellationToken ct) => TypedResults.Ok((await svc.ForLoanAsync(id, ct)).Select(ToResponse).ToList()))
            .RequirePermission(Permissions.Loans.View).WithName("ListLoanAdjustments");
        g.MapPost("/adjustments/{adjustmentId:guid}/approve", async (Guid adjustmentId, NotesRequest r, LoanAdjustmentService svc, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await svc.ApproveAsync(adjustmentId, r.Notes, user.UserId, ct))))
            .RequirePermission(Permissions.Loans.Approve).WithName("ApproveLoanAdjustment");
        g.MapPost("/adjustments/{adjustmentId:guid}/reject", async (Guid adjustmentId, ReasonRequest r, LoanAdjustmentService svc, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await svc.RejectAsync(adjustmentId, r.Reason, user.UserId, ct))))
            .RequirePermission(Permissions.Loans.Approve).WithName("RejectLoanAdjustment");
        g.MapGet("/bureau-consent", (IOptions<LendingSettings> settings) => TypedResults.Ok(new BureauConsentInfo(settings.Value.CreditBureau.RequireConsent, settings.Value.CreditBureau.ConsentText)))
            .RequirePermission(Permissions.Loans.View).WithName("GetBureauConsentText");
        g.MapPost("/maintenance/run", async (LendingMaintenanceService maintenance, IAuditLogger audit, ICurrentUser user, CancellationToken ct) =>
        {
            var result = await maintenance.RunOnceAsync(ct);
            await audit.RecordAsync(new AuditEvent("loans.maintenance.run", "LendingMaintenance", DateTime.UtcNow.ToString("O"), user.UserId, AuditDetails.New().With("result", result).ToJson()), ct);
            return TypedResults.Ok(result);
        })
            .RequirePermission(Permissions.Admin.ConfigManage).WithName("RunLendingMaintenance");

        // ---- Member self-service: the member behind the token, nobody else ----
        var self = app.MapGroup("/api/self/loans").WithTags("Self-service");
        self.MapGet("", async (ILendingService lending, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(await lending.GetMemberLoansAsync(user.RequireMemberId(), ct)))
            .RequirePermission(Permissions.Self.LoansView).WithName("GetMyLoans");
        self.MapGet("/bureau-consent", (IOptions<LendingSettings> settings) => TypedResults.Ok(new BureauConsentInfo(settings.Value.CreditBureau.RequireConsent, settings.Value.CreditBureau.ConsentText)))
            .RequirePermission(Permissions.Self.LoansApply).WithName("GetMyBureauConsentText");
        self.MapPost("", async (SelfApplyLoanRequest r, LoanService loans, ILendingService lending, ICurrentUser user, CancellationToken ct) =>
        {
            var loan = await loans.ApplyAsync(user.RequireMemberId(), r.ProductCode, r.Amount, r.TermMonths, r.Purpose, null, user.UserId, ct, r.BureauConsent);
            return TypedResults.Created($"/api/self/loans", (await lending.GetMemberLoansAsync(user.RequireMemberId(), ct)).First(l => l.Id == loan.Id));
        }).RequirePermission(Permissions.Self.LoansApply).WithName("ApplyForMyLoan");

        // ---- Credit scoring: advisory only; approval remains the N-of-M maker-checker workflow ----
        g.MapGet("/{id:guid}/score", async Task<Results<Ok<CreditScoreResponse>, NotFound>> (Guid id, CreditScoringService scoring, CancellationToken ct) =>
        {
            var latest = await scoring.GetLatestAsync(id, ct);
            return latest is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(latest));
        }).RequirePermission(Permissions.Loans.View).WithName("GetLoanCreditScore");
        g.MapGet("/{id:guid}/score/history", async (Guid id, CreditScoringService scoring, CancellationToken ct) => TypedResults.Ok((await scoring.HistoryAsync(id, ct)).Select(ToResponse).ToList()))
            .RequirePermission(Permissions.Loans.View).WithName("GetLoanCreditScoreHistory");
        g.MapPost("/{id:guid}/score", async (Guid id, CreditScoringService scoring, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await scoring.ScoreLoanAsync(id, ScoreStage.Manual, user.UserId, ct))))
            .RequirePermission(Permissions.Loans.Appraise).WithName("RecomputeLoanCreditScore");

        var scoringGroup = app.MapGroup("/api/loans/scoring").WithTags("Credit scoring");
        scoringGroup.MapGet("/scorecard", async (CreditScoringService scoring, CancellationToken ct) => TypedResults.Ok(ToResponse(await scoring.GetScorecardAsync(ct))))
            .RequirePermission(Permissions.Loans.View).WithName("GetScorecard");
        scoringGroup.MapPut("/scorecard", async (SetScorecardRequest r, CreditScoringService scoring, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await scoring.SetScorecardAsync(r.Factors, r.ApproveThreshold, r.ReferThreshold, r.DeclineIfBureauListed, r.Source, user.UserId, ct))))
            .RequirePermission(Permissions.Loans.ScoringManage).WithName("SetScorecard");
        scoringGroup.MapGet("/factors", () => TypedResults.Ok(ScoringFactors.All)).RequirePermission(Permissions.Loans.View).WithName("ListScoringFactors");
        scoringGroup.MapGet("/bureau", (ICreditBureau bureau) => TypedResults.Ok(new { bureau.Name })).RequirePermission(Permissions.Loans.View).WithName("GetCreditBureauInfo");
        g.MapPost("", async (ApplyLoanRequest r, LoanService loans, LendingDbContext db, ILedgerService ledger, IClock clock, ICurrentUser user, CancellationToken ct) =>
        {
            var loan = await loans.ApplyAsync(r.MemberId, r.ProductCode, r.Amount, r.TermMonths, r.Purpose, r.DisbursementAccountNumber, user.UserId, ct, r.BureauConsent);
            return TypedResults.Created($"/api/loans/{loan.Id}", await ToResponse(loan, await db.Products.FirstAsync(p => p.Id == loan.ProductId, ct), ledger, clock, ct));
        }).RequirePermission(Permissions.Loans.Originate).WithName("ApplyForLoan");
        g.MapPost("/{id:guid}/guarantors", async (Guid id, AddGuarantorRequest r, LoanService loans, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok((await loans.AddGuarantorAsync(id, r.GuarantorMemberId, r.Amount, user.UserId, ct)).Guarantors.Select(ToResponse).ToList()))
            .RequirePermission(Permissions.Loans.Originate).WithName("AddLoanGuarantor");
        g.MapPost("/{id:guid}/guarantors/{guarantorId:guid}/accept", async (Guid id, Guid guarantorId, LoanService loans, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok((await loans.AcceptGuaranteeAsync(id, guarantorId, user.UserId, ct)).Guarantors.Select(ToResponse).ToList()))
            .RequirePermission(Permissions.Loans.Originate).WithName("AcceptLoanGuarantee");
        g.MapPost("/{id:guid}/guarantors/{guarantorId:guid}/decline", async (Guid id, Guid guarantorId, LoanService loans, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok((await loans.DeclineGuaranteeAsync(id, guarantorId, user.UserId, ct)).Guarantors.Select(ToResponse).ToList()))
            .RequirePermission(Permissions.Loans.Originate).WithName("DeclineLoanGuarantee");
        g.MapPost("/{id:guid}/appraise", async (Guid id, NotesRequest r, LoanService loans, CreditScoringService scoring, LendingDbContext db, ILedgerService ledger, IClock clock, ICurrentUser user, CancellationToken ct) =>
        {
            var loan = await loans.AppraiseAsync(id, r.Notes ?? string.Empty, user.UserId, ct);
            return TypedResults.Ok(await ToResponse(loan, await db.Products.FirstAsync(p => p.Id == loan.ProductId, ct), ledger, clock, ct, await scoring.GetLatestAsync(id, ct)));
        }).RequirePermission(Permissions.Loans.Appraise).WithName("AppraiseLoan");
        g.MapPost("/{id:guid}/approve", async (Guid id, NotesRequest r, LoanService loans, LendingDbContext db, ILedgerService ledger, IClock clock, ICurrentUser user, CancellationToken ct) =>
        {
            var loan = await loans.ApproveAsync(id, r.Notes, user.UserId, ct);
            return TypedResults.Ok(await ToResponse(loan, await db.Products.FirstAsync(p => p.Id == loan.ProductId, ct), ledger, clock, ct));
        }).RequirePermission(Permissions.Loans.Approve).WithName("ApproveLoan");
        g.MapPost("/{id:guid}/reject", async (Guid id, ReasonRequest r, LoanService loans, LendingDbContext db, ILedgerService ledger, IClock clock, ICurrentUser user, CancellationToken ct) =>
        {
            var loan = await loans.RejectAsync(id, r.Reason, user.UserId, ct);
            return TypedResults.Ok(await ToResponse(loan, await db.Products.FirstAsync(p => p.Id == loan.ProductId, ct), ledger, clock, ct));
        }).RequirePermission(Permissions.Loans.Approve).WithName("RejectLoan");
        g.MapPost("/{id:guid}/disburse", async (Guid id, LoanService loans, LendingDbContext db, ILedgerService ledger, IClock clock, ICurrentUser user, CancellationToken ct) =>
        {
            var loan = await loans.DisburseAsync(id, user.UserId, null, ct);
            return TypedResults.Ok(await ToResponse(loan, await db.Products.FirstAsync(p => p.Id == loan.ProductId, ct), ledger, clock, ct));
        }).RequirePermission(Permissions.Loans.Disburse).WithName("DisburseLoan");
        g.MapPost("/{loanNumber}/repayments", async (string loanNumber, RepayRequest r, RepaymentService repayments, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(await repayments.RepayAsync(new RepaymentCommand(loanNumber, r.Amount, r.Channel, r.Reference, r.Narrative, user.UserId), ct)))
            .RequirePermission(Permissions.Loans.Repay).WithName("RepayLoan");
        g.MapPost("/accrue-interest", async (RepaymentService repayments, IClock clock, ICurrentUser user, DateOnly? asOf, CancellationToken ct) =>
            TypedResults.Ok(new { Accrued = await repayments.AccrueInterestAsync(asOf ?? clock.Today, user.UserId, ct) }))
            .RequirePermission(Permissions.Loans.ProvisioningManage).WithName("AccrueLoanInterest");

        var prov = app.MapGroup("/api/loans/provisioning").WithTags("Provisioning & NPL aging");
        prov.MapGet("/config", async (ProvisioningService svc, CancellationToken ct) => TypedResults.Ok(ToResponse(await svc.GetConfigAsync(ct))))
            .RequirePermission(Permissions.Loans.View).WithName("GetProvisioningConfig");
        prov.MapPut("/config", async (SetProvisioningConfigRequest r, ProvisioningService svc, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await svc.SetConfigAsync(r.Buckets, r.Source, user.UserId, ct))))
            .RequirePermission(Permissions.Loans.ProvisioningManage).WithName("SetProvisioningConfig");
        prov.MapGet("/aging", async (ProvisioningService svc, IClock clock, DateOnly? asOf, CancellationToken ct) => TypedResults.Ok(await svc.AgingAsync(asOf ?? clock.Today, ct)))
            .RequirePermission(Permissions.Loans.View).WithName("GetLoanAging");
        prov.MapGet("/runs", async (LendingDbContext db, CancellationToken ct) => TypedResults.Ok((await db.ProvisioningRuns.AsNoTracking().OrderByDescending(r => r.AsOf).ToListAsync(ct)).Select(ToResponse).ToList()))
            .RequirePermission(Permissions.Loans.View).WithName("ListProvisioningRuns");
        prov.MapPost("/runs", async (ProvisioningService svc, IClock clock, ICurrentUser user, DateOnly? asOf, CancellationToken ct) =>
        {
            var run = await svc.ComputeRunAsync(asOf ?? clock.Today, user.UserId, ct);
            return TypedResults.Created($"/api/loans/provisioning/runs/{run.Id}", ToResponse(run));
        }).RequirePermission(Permissions.Loans.ProvisioningManage).WithName("ComputeProvisioningRun");
        prov.MapPost("/runs/{id:guid}/approve", async (Guid id, ProvisioningService svc, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await svc.ApproveRunAsync(id, user.UserId, ct))))
            .RequirePermission(Permissions.Ledger.JournalApprove).WithName("ApproveProvisioningRun");
        prov.MapPost("/runs/{id:guid}/reject", async (Guid id, ReasonRequest r, ProvisioningService svc, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await svc.RejectRunAsync(id, r.Reason, user.UserId, ct))))
            .RequirePermission(Permissions.Ledger.JournalApprove).WithName("RejectProvisioningRun");
    }

    private static LoanProductResponse ToResponse(LoanProduct p) => new(p.Id, p.Code, p.Name, p.Description, p.Segment, p.ControlGlAccountCode, p.InterestIncomeGlAccountCode, p.InterestReceivableGlAccountCode, p.FeeIncomeGlAccountCode, p.ProvisionGlAccountCode, p.ProvisionExpenseGlAccountCode,
        p.InterestRateBps, p.InterestMethod, p.MinAmount, p.MaxAmount, p.MinTermMonths, p.MaxTermMonths, p.DepositMultiplier, p.MinMembershipMonths, p.ProcessingFeeBps, p.RequiresGuarantors, p.MinGuarantors, p.CommitteeThreshold, p.CommitteeApprovalsRequired, p.GracePeriodDays, p.IsActive,
        p.Category, ProductListing.From(p.Listing));
    private static EligibilityResponse ToResponse(EligibilitySnapshot e) => new(e.BosaDeposits, e.Shares, e.DepositMultiplier, e.MaxEligibleAmount, e.MembershipMonths, e.ContributionMonths, e.ExistingOutstanding);
    private static GuarantorResponse ToResponse(LoanGuarantor g) => new(g.Id, g.GuarantorMemberId, g.DepositsAccountNumber, g.AmountGuaranteed, g.Status, g.AddedAt, g.AcceptedAt);
    private static CreditScoreSummary Summary(LoanCreditScore s) => new(s.Score, s.Grade, s.Recommendation, s.BureauStatus, s.Stage, s.ComputedAt);
    private static CreditScoreResponse ToResponse(LoanCreditScore s) => new(s.Id, s.LoanId, s.Stage, s.Score, s.Grade, s.Recommendation, s.RecommendationReason, s.BureauStatus, s.BureauReference, s.BureauNarrative,
        CreditScoringService.ParseFactors(s.FactorsJson), s.ScorecardUpdatedAt, s.ComputedAt, s.ComputedByUserId);
    private static ScorecardResponse ToResponse(Scorecard c) => new(c.Factors.Select(f => new ScorecardFactorDraft(f.Key, f.MaxPoints)).ToList(), c.TotalPoints, c.ApproveThreshold, c.ReferThreshold, c.DeclineIfBureauListed, c.Source, c.UpdatedAt, c.UpdatedByUserId);
    private static LoanAdjustmentResponse ToResponse(LoanAdjustment a) => new(a.Id, a.LoanId, a.LoanNumber, a.Kind, a.Status, a.Reason, a.RequestedByUserId, a.RequestedAt, a.DecidedByUserId, a.DecidedAt, a.DecisionNotes, a.NewTermMonths, a.NewInterestRateBps, a.PrincipalWrittenOff, a.InterestReversed, a.JournalEntryId);
    private static async Task<LoanResponse> ToResponse(Loan l, LoanProduct product, ILedgerService ledger, IClock clock, CancellationToken ct, LoanCreditScore? score = null, IReadOnlyList<LoanAdjustment>? adjustments = null)
    {
        var outstanding = l.LedgerAccountNumber is null ? 0m : (await ledger.FindAccountAsync(l.LedgerAccountNumber, ct))?.Balance ?? 0m;
        return new LoanResponse(l.Id, l.LoanNumber, l.MemberId, l.ProductCode, l.Segment, l.Amount, l.TermMonths, l.InterestRateBps, l.InterestMethod, l.Purpose, l.Status, ToResponse(l.Eligibility),
            l.DisbursementAccountNumber, l.PledgedDepositsAccountNumber, l.PledgedDepositsAmount, l.LedgerAccountNumber, outstanding, l.Status == LoanStatus.Active ? l.ArrearsAmount(clock.Today) : 0m, l.Status == LoanStatus.Active ? l.DaysInArrears(clock.Today, product.GracePeriodDays) : 0,
            l.AppliedAt, l.AppliedByUserId, l.AppraisedByUserId, l.AppraisalNotes, l.ApprovedAt, l.DisbursementDate, l.DisbursedByUserId, l.ProcessingFee, l.RejectionReason, product.ApprovalsRequiredFor(l.Amount),
            l.Guarantors.Select(ToResponse).ToList(),
            l.Approvals.OrderBy(a => a.DecidedAt).Select(a => new ApprovalResponse(a.ApproverUserId, a.Decision, a.Notes, a.DecidedAt)).ToList(),
            l.Schedule.OrderBy(i => i.Number).Select(i => new InstallmentResponse(i.Number, i.DueDate, i.PrincipalDue, i.InterestDue, i.PrincipalPaid, i.InterestPaid, i.Outstanding, i.InterestAccrued, i.Status)).ToList(),
            score is null ? null : Summary(score), l.BureauConsentAt, l.RestructureCount, (adjustments ?? []).Select(ToResponse).ToList());
    }
    private static ProvisioningConfigResponse ToResponse(ProvisioningConfig c) => new(c.Buckets.Select(b => new AgingBucketDraft(b.Name, b.MinDaysInArrears, b.ProvisionRateBps)).ToList(), c.Source, c.UpdatedAt, c.UpdatedByUserId);
    private static ProvisioningRunResponse ToResponse(ProvisioningRun r) => new(r.Id, r.AsOf, r.Status, r.ComputedByUserId, r.ComputedAt, r.ApprovedByUserId, r.ApprovedAt, r.RejectionReason, r.TotalOutstanding, r.TotalProvisionRequired,
        r.Lines.OrderByDescending(l => l.DaysInArrears).Select(l => new ProvisioningLineResponse(l.LoanNumber, l.MemberId, l.ProductCode, l.Segment, l.DaysInArrears, l.Bucket, l.ProvisionRateBps, l.OutstandingPrincipal, l.ArrearsAmount, l.ProvisionRequired)).ToList());
}
