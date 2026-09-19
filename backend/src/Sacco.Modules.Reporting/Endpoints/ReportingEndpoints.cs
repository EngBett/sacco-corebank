using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Reporting.Application;
using Sacco.Modules.Reporting.Domain;
using Sacco.Modules.Reporting.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Sacco.Shared.Http;
using Sacco.Shared.Time;

namespace Sacco.Modules.Reporting.Endpoints;

public sealed record GenerateReturnRequest(DateOnly PeriodStart, DateOnly PeriodEnd);
public sealed record SubmitReturnRequest(string SubmissionReference);
public sealed record ReasonRequest(string Reason);
public sealed record AddRecipientRequest(string Email, string Name);
public sealed record StatutoryReturnSummary(Guid Id, DateOnly PeriodStart, DateOnly PeriodEnd, ReturnStatus Status, Guid GeneratedByUserId, DateTimeOffset GeneratedAt, Guid? SubmittedByUserId, DateTimeOffset? SubmittedAt, string? SubmissionReference, bool IsReconciled, string ReconciliationNotes);
public sealed record StatutoryReturnDetail(StatutoryReturnSummary Summary, StatutoryReturnPackage Package);

public sealed class ReportingEndpoints : IModuleEndpoints
{
    public void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/reporting").WithTags("Regulatory reporting");

        g.MapGet("/financial-position", async (StatutoryReportService svc, IClock clock, Segment? segment, DateOnly? asOf, CancellationToken ct) => TypedResults.Ok(await svc.FinancialPositionAsync(segment, asOf ?? clock.Today, ct)))
            .RequirePermission(Permissions.Reporting.View).WithName("GetFinancialPosition");
        g.MapGet("/income-statement", async (StatutoryReportService svc, IClock clock, Segment? segment, DateOnly? from, DateOnly? to, CancellationToken ct) =>
            TypedResults.Ok(await svc.IncomeStatementAsync(segment, from ?? new DateOnly(clock.Today.Year, 1, 1), to ?? clock.Today, ct)))
            .RequirePermission(Permissions.Reporting.View).WithName("GetIncomeStatement");
        g.MapGet("/capital-adequacy", async (StatutoryReportService svc, IClock clock, DateOnly? asOf, CancellationToken ct) => TypedResults.Ok(await svc.CapitalAdequacyAsync(asOf ?? clock.Today, ct)))
            .RequirePermission(Permissions.Reporting.View).WithName("GetCapitalAdequacy");
        g.MapGet("/liquidity", async (StatutoryReportService svc, IClock clock, DateOnly? asOf, CancellationToken ct) => TypedResults.Ok(await svc.LiquidityAsync(asOf ?? clock.Today, ct)))
            .RequirePermission(Permissions.Reporting.View).WithName("GetLiquidity");
        g.MapGet("/large-exposures", async (StatutoryReportService svc, IClock clock, DateOnly? asOf, CancellationToken ct) => TypedResults.Ok(await svc.LargeExposuresAsync(asOf ?? clock.Today, ct)))
            .RequirePermission(Permissions.Reporting.View).WithName("GetLargeExposures");
        g.MapGet("/portfolio-quality", async (Sacco.Shared.Lending.ILendingService lending, IClock clock, DateOnly? asOf, CancellationToken ct) => TypedResults.Ok(await lending.GetPortfolioQualityAsync(asOf ?? clock.Today, ct)))
            .RequirePermission(Permissions.Reporting.View).WithName("GetPortfolioQuality");
        g.MapGet("/preview", async (StatutoryReportService svc, IClock clock, DateOnly? periodStart, DateOnly? periodEnd, CancellationToken ct) =>
        {
            var end = periodEnd ?? clock.Today;
            return TypedResults.Ok(await svc.BuildPackageAsync(periodStart ?? new DateOnly(end.Year, end.Month, 1).AddMonths(-2), end, ct));
        }).RequirePermission(Permissions.Reporting.View).WithName("PreviewStatutoryReturn");

        var r = app.MapGroup("/api/reporting/statutory-returns").WithTags("Statutory returns");
        r.MapGet("", async (ReportingDbContext db, CancellationToken ct) => TypedResults.Ok((await db.Returns.AsNoTracking().OrderByDescending(x => x.PeriodEnd).ToListAsync(ct)).Select(Summary).ToList()))
            .RequirePermission(Permissions.Reporting.View).WithName("ListStatutoryReturns");
        r.MapGet("/{id:guid}", async (Guid id, StatutoryReportService svc, CancellationToken ct) =>
        {
            var ret = await svc.GetAsync(id, ct);
            return TypedResults.Ok(new StatutoryReturnDetail(Summary(ret), StatutoryReportService.Deserialize(ret.Package)));
        }).RequirePermission(Permissions.Reporting.View).WithName("GetStatutoryReturn");
        r.MapGet("/{id:guid}/export.csv", async (Guid id, StatutoryReportService svc, IAuditLogger audit, ICurrentUser user, CancellationToken ct) =>
        {
            var ret = await svc.GetAsync(id, ct);
            var csv = StatutoryReturnCsv.Render(StatutoryReportService.Deserialize(ret.Package));
            await audit.RecordAsync(new AuditEvent("reporting.return.exported", "StatutoryReturn", id.ToString(), user.UserId,
                AuditDetails.New().With("periodEnd", ret.PeriodEnd.ToString("yyyy-MM-dd")).With("format", "csv").ToJson()), ct);
            return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"sasra-return-{ret.PeriodEnd:yyyy-MM-dd}.csv");
        }).RequirePermission(Permissions.Reporting.View).WithName("ExportStatutoryReturnCsv");
        r.MapPost("", async (GenerateReturnRequest req, StatutoryReportService svc, ICurrentUser user, CancellationToken ct) =>
        {
            var ret = await svc.GenerateAsync(req.PeriodStart, req.PeriodEnd, user.UserId, ct);
            return TypedResults.Created($"/api/reporting/statutory-returns/{ret.Id}", new StatutoryReturnDetail(Summary(ret), StatutoryReportService.Deserialize(ret.Package)));
        }).RequirePermission(Permissions.Reporting.StatutoryGenerate).WithName("GenerateStatutoryReturn");
        r.MapPost("/{id:guid}/submit", async (Guid id, SubmitReturnRequest req, StatutoryReportService svc, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(Summary(await svc.SubmitAsync(id, req.SubmissionReference, user.UserId, ct))))
            .RequirePermission(Permissions.Reporting.StatutorySubmit).WithName("SubmitStatutoryReturn");
        r.MapPost("/{id:guid}/withdraw", async (Guid id, ReasonRequest req, StatutoryReportService svc, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(Summary(await svc.WithdrawAsync(id, req.Reason, user.UserId, ct))))
            .RequirePermission(Permissions.Reporting.StatutoryGenerate).WithName("WithdrawStatutoryReturn");

        // ---- Nightly PDF digest (ADR 0013): who receives it, and an on-demand run/preview for demos ----
        var d = app.MapGroup("/api/reporting/daily-digest").WithTags("Daily digest");
        d.MapGet("/recipients", async (DailyDigestService svc, CancellationToken ct) => TypedResults.Ok(await svc.ListRecipientsAsync(ct)))
            .RequirePermission(Permissions.Reporting.RecipientsManage).WithName("ListDigestRecipients");
        d.MapPost("/recipients", async (AddRecipientRequest req, DailyDigestService svc, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Created("/api/reporting/daily-digest/recipients", await svc.AddRecipientAsync(req.Email, req.Name, user.UserId, ct)))
            .RequirePermission(Permissions.Reporting.RecipientsManage).WithName("AddDigestRecipient");
        d.MapDelete("/recipients/{id:guid}", async (Guid id, DailyDigestService svc, ICurrentUser user, CancellationToken ct) =>
        {
            await svc.RemoveRecipientAsync(id, user.UserId, ct);
            return TypedResults.NoContent();
        }).RequirePermission(Permissions.Reporting.RecipientsManage).WithName("RemoveDigestRecipient");
        d.MapGet("/preview.pdf", async (DailyDigestService svc, IAuditLogger audit, ICurrentUser user, CancellationToken ct) =>
        {
            var (pdf, fileName, _) = await svc.BuildAsync(ct);
            await audit.RecordAsync(new AuditEvent("reporting.digest.downloaded", "DailyDigest", fileName, user.UserId,
                AuditDetails.New().With("bytes", pdf.Length).ToJson()), ct);
            return Results.File(pdf, "application/pdf", fileName);
        }).RequirePermission(Permissions.Reporting.RecipientsManage).WithName("PreviewDailyDigest");
        d.MapPost("/run", async (DailyDigestService svc, IAuditLogger audit, ICurrentUser user, CancellationToken ct) =>
        {
            var result = await svc.RunForCurrentTenantAsync(ct);
            await audit.RecordAsync(new AuditEvent("reporting.digest.sent", "DailyDigest", DateTime.UtcNow.ToString("yyyy-MM-dd"), user.UserId,
                AuditDetails.New().With("recipients", result.RecipientCount).With("sent", result.Sent).ToJson(), Outcome: result.Sent ? AuditOutcome.Success : AuditOutcome.Failure), ct);
            return TypedResults.Ok(result);
        })
            .RequirePermission(Permissions.Reporting.RecipientsManage).WithName("RunDailyDigestNow");
    }

    private static StatutoryReturnSummary Summary(StatutoryReturn r) => new(r.Id, r.PeriodStart, r.PeriodEnd, r.Status, r.GeneratedByUserId, r.GeneratedAt, r.SubmittedByUserId, r.SubmittedAt, r.SubmissionReference, r.IsReconciled, r.ReconciliationNotes);
}
