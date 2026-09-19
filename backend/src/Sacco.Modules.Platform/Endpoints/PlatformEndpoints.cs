using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Platform.Application;
using Sacco.Modules.Platform.Domain;
using Sacco.Modules.Platform.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Auth;
using Sacco.Shared.Http;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Platform.Endpoints;

public sealed record TenantBrandingResponse(string Slug, string Name, string ShortName, string PrimaryColor, string SecondaryColor, string AccentColor, string? LogoUrl, string Tagline, string SupportEmail, string SupportPhone, string? FaviconUrl, string? LightModeLogoUrl, string? DarkModeLogoUrl,
    /// <summary>Set only on a demonstration deployment (configuration section <c>Demo</c>); both websites show a banner when it is present.</summary>
    DemoNotice? Demo);
public sealed record UpdateBrandingRequest(string PrimaryColor, string SecondaryColor, string AccentColor, string? LogoUrl, string Tagline, string SupportEmail, string SupportPhone, string? FaviconUrl = null, string? LightModeLogoUrl = null, string? DarkModeLogoUrl = null);
public sealed record BranchResponse(Guid Id, string Code, string Name, bool IsHeadOffice, string? County, string? Town, string? PhysicalAddress, string? PhoneNumber, string? Email, int DisplayOrder, bool IsActive);
public sealed record SaveBranchRequest(string Code, string Name, bool IsHeadOffice, string? County, string? Town, string? PhysicalAddress, string? PhoneNumber, string? Email, int DisplayOrder, bool IsActive = true);
public sealed record PublicBranch(string Code, string Name, bool IsHeadOffice, string? County, string? Town, string? PhysicalAddress, string? PhoneNumber, string? Email);
public sealed record PublicServiceResponse(Guid Id, string Name, string? Description, string Icon, int DisplayOrder, bool IsActive);
public sealed record SavePublicServiceRequest(string Name, string? Description, string Icon, int DisplayOrder, bool IsActive = true);
public sealed record AuditLogResponse(Guid Id, DateTimeOffset OccurredAt, string Action, string EntityType, string EntityId, Guid ActorUserId, string? Details, string? CorrelationId,
    string? ActorName, AuditOutcome Outcome, string? IpAddress, string? UserAgent, Guid? BranchId, string Hash);

public sealed class PlatformEndpoints : IModuleEndpoints
{
    /// <summary>Per-IP limiter for the public website's read-only catalogue calls (registered by the API host).</summary>
    public const string PublicReadRateLimitPolicy = "public-read";

    public void Map(IEndpointRouteBuilder app)
    {
        var pub = app.MapGroup("/api/public/tenant").WithTags("Tenant (public)");

        // Public: both Next.js apps call this server-side to theme the first paint (ADR 0006).
        pub.MapGet("/branding", (HttpContext http, DemoSettings demo) =>
        {
            var t = (TenantInfo)http.Items["Tenant"]!;
            var b = t.Branding;
            return TypedResults.Ok(new TenantBrandingResponse(t.Slug, t.Name, t.ShortName, b.PrimaryColor, b.SecondaryColor, b.AccentColor, b.LogoUrl, b.Tagline, b.SupportEmail, b.SupportPhone, b.FaviconUrl, b.LightModeLogoUrl, b.DarkModeLogoUrl, demo.ToNotice()));
        }).WithName("GetTenantBranding");

        // Public: the member services a SACCO lists on its website, in its chosen order.
        app.MapGet("/api/public/services", async (PlatformDbContext db, CancellationToken ct) =>
            TypedResults.Ok(await db.PublicServices.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
                .Select(s => new PublicServiceResponse(s.Id, s.Name, s.Description, s.Icon, s.DisplayOrder, s.IsActive)).ToListAsync(ct)))
            .RequireRateLimiting(PublicReadRateLimitPolicy).WithTags("Public").WithName("ListPublicServices");

        // Public: where members can find the SACCO.
        app.MapGet("/api/public/branches", async (BranchService branches, CancellationToken ct) =>
            TypedResults.Ok((await branches.ListForAdminAsync(ct)).Where(b => b.IsActive)
                .Select(b => new PublicBranch(b.Code, b.Name, b.IsHeadOffice, b.County, b.Town, b.PhysicalAddress, b.PhoneNumber, b.Email)).ToList()))
            .RequireRateLimiting(PublicReadRateLimitPolicy).WithTags("Public").WithName("ListPublicBranches");

        var admin = app.MapGroup("/api/admin").WithTags("Platform admin");

        admin.MapGet("/branches", async (BranchService branches, CancellationToken ct) =>
            TypedResults.Ok((await branches.ListForAdminAsync(ct)).Select(ToResponse).ToList()))
            // Anyone who assigns, filters by or reports on offices needs to be able to name them — an auditor included.
            .RequireAnyPermission(Permissions.Admin.TenantManage, Permissions.Admin.UsersManage, Permissions.Admin.UsersInvite, Permissions.Members.Create, Permissions.Admin.AuditView)
            .WithName("ListBranches");

        admin.MapPost("/branches", async (SaveBranchRequest r, BranchService branches, ICurrentUser user, CancellationToken ct) =>
        {
            var branch = await branches.CreateAsync(r.Code, r.Name, r.IsHeadOffice, r.County, r.Town, r.PhysicalAddress, r.PhoneNumber, r.Email, r.DisplayOrder, user.UserId, ct);
            return TypedResults.Created($"/api/admin/branches/{branch.Id}", ToResponse(branch));
        }).RequirePermission(Permissions.Admin.TenantManage).WithName("CreateBranch");

        admin.MapPut("/branches/{id:guid}", async (Guid id, SaveBranchRequest r, BranchService branches, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await branches.UpdateAsync(id, r.Code, r.Name, r.IsHeadOffice, r.County, r.Town, r.PhysicalAddress, r.PhoneNumber, r.Email, r.DisplayOrder, r.IsActive, user.UserId, ct))))
            .RequirePermission(Permissions.Admin.TenantManage).WithName("UpdateBranch");

        admin.MapGet("/public-services", async (PlatformDbContext db, CancellationToken ct) =>
            TypedResults.Ok(await db.PublicServices.AsNoTracking().OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name)
                .Select(s => new PublicServiceResponse(s.Id, s.Name, s.Description, s.Icon, s.DisplayOrder, s.IsActive)).ToListAsync(ct)))
            .RequirePermission(Permissions.Admin.TenantManage).WithName("ListPublicServicesAdmin");

        admin.MapPost("/public-services", async (SavePublicServiceRequest req, PlatformDbContext db, ITenantContext tenant, IAuditLogger audit, ICurrentUser user, CancellationToken ct) =>
        {
            var service = PublicService.Create(Sacco.Shared.Domain.Ids.New(), tenant.TenantId, req.Name, req.Description, req.Icon, req.DisplayOrder);
            if (!req.IsActive) service.Update(req.Name, req.Description, req.Icon, req.DisplayOrder, isActive: false);
            db.PublicServices.Add(service);
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new AuditEvent("platform.public_service.created", nameof(PublicService), service.Id.ToString(), user.UserId), ct);
            return TypedResults.Created($"/api/admin/public-services/{service.Id}", new PublicServiceResponse(service.Id, service.Name, service.Description, service.Icon, service.DisplayOrder, service.IsActive));
        }).RequirePermission(Permissions.Admin.TenantManage).WithName("CreatePublicService");

        admin.MapPut("/public-services/{id:guid}", async Task<Results<Ok<PublicServiceResponse>, NotFound>> (Guid id, SavePublicServiceRequest req, PlatformDbContext db, IAuditLogger audit, ICurrentUser user, CancellationToken ct) =>
        {
            var service = await db.PublicServices.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (service is null) return TypedResults.NotFound();
            service.Update(req.Name, req.Description, req.Icon, req.DisplayOrder, req.IsActive);
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new AuditEvent("platform.public_service.updated", nameof(PublicService), service.Id.ToString(), user.UserId), ct);
            return TypedResults.Ok(new PublicServiceResponse(service.Id, service.Name, service.Description, service.Icon, service.DisplayOrder, service.IsActive));
        }).RequirePermission(Permissions.Admin.TenantManage).WithName("UpdatePublicService");

        admin.MapDelete("/public-services/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, PlatformDbContext db, IAuditLogger audit, ICurrentUser user, CancellationToken ct) =>
        {
            var service = await db.PublicServices.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (service is null) return TypedResults.NotFound();
            db.PublicServices.Remove(service);
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new AuditEvent("platform.public_service.deleted", nameof(PublicService), id.ToString(), user.UserId), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Permissions.Admin.TenantManage).WithName("DeletePublicService");

        admin.MapPut("/tenant/branding", async (UpdateBrandingRequest req, PlatformDbContext db, ITenantContext tenant, ITenantDirectory directory, IAuditLogger audit, ICurrentUser user, CancellationToken ct) =>
        {
            var t = await db.Tenants.FirstAsync(x => x.Id == tenant.TenantId, ct);
            var before = t.Branding;
            var changes = AuditDetails.New()
                .Changed("primaryColor", before.PrimaryColor, req.PrimaryColor)
                .Changed("secondaryColor", before.SecondaryColor, req.SecondaryColor)
                .Changed("accentColor", before.AccentColor, req.AccentColor)
                .Changed("logoUrl", before.LogoUrl, req.LogoUrl)
                .Changed("faviconUrl", before.FaviconUrl, req.FaviconUrl)
                .Changed("lightModeLogoUrl", before.LightModeLogoUrl, req.LightModeLogoUrl)
                .Changed("darkModeLogoUrl", before.DarkModeLogoUrl, req.DarkModeLogoUrl)
                .Changed("tagline", before.Tagline, req.Tagline)
                .Changed("supportEmail", before.SupportEmail, req.SupportEmail)
                .Changed("supportPhone", before.SupportPhone, req.SupportPhone);
            t.UpdateBranding(new TenantBranding
            {
                PrimaryColor = req.PrimaryColor, SecondaryColor = req.SecondaryColor, AccentColor = req.AccentColor,
                LogoUrl = req.LogoUrl, FaviconUrl = req.FaviconUrl, LightModeLogoUrl = req.LightModeLogoUrl, DarkModeLogoUrl = req.DarkModeLogoUrl, Tagline = req.Tagline, SupportEmail = req.SupportEmail, SupportPhone = req.SupportPhone,
            });
            await db.SaveChangesAsync(ct);
            directory.Invalidate(t.Slug);
            await audit.RecordAsync(new AuditEvent("platform.branding.updated", nameof(Tenant), t.Slug, user.UserId, changes.ToJson()), ct);
            return TypedResults.NoContent();
        }).RequirePermission(Permissions.Admin.TenantManage).WithName("UpdateTenantBranding");

        // ---- Audit trail (ADR 0019) ----
        admin.MapGet("/audit-log", async (PlatformDbContext db, string? action, string? entityType, string? entityId, Guid? actorUserId, Guid? branchId,
            string? outcome, string? from, string? to, string? search, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 200);
            var q = AuditQuery(db, action, entityType, entityId, actorUserId, branchId, Outcome(outcome), Instant(from, "from"), Instant(to, "to"), search);
            var total = await q.CountAsync(ct);
            var items = await q.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return TypedResults.Ok(new PagedResult<AuditLogResponse>(items.Select(ToResponse).ToList(), page, pageSize, total));
        }).RequirePermission(Permissions.Admin.AuditView).WithName("GetAuditLog");

        // The same filters as the list, streamed as CSV for an inspection or an external archive.
        admin.MapGet("/audit-log/export.csv", async (HttpContext http, PlatformDbContext db, string? action, string? entityType, string? entityId, Guid? actorUserId, Guid? branchId,
            string? outcome, string? from, string? to, string? search, IAuditLogger audit, ICurrentUser user, CancellationToken ct) =>
        {
            var (parsedOutcome, fromInstant, toInstant) = (Outcome(outcome), Instant(from, "from"), Instant(to, "to"));
            var rows = await AuditQuery(db, action, entityType, entityId, actorUserId, branchId, parsedOutcome, fromInstant, toInstant, search)
                .OrderBy(a => a.OccurredAt).ThenBy(a => a.Id).Take(50_000).ToListAsync(ct);
            // The natural key of an export is the period it covers; an open end reads as "everything".
            var period = fromInstant is null && toInstant is null ? "all" : $"{(fromInstant is { } f ? f.ToString("O") : "beginning")}..{(toInstant is { } t ? t.ToString("O") : "now")}";
            await audit.RecordAsync(new AuditEvent("platform.audit_log.exported", "AuditLog", period, user.UserId,
                AuditDetails.New().With("rows", rows.Count).With("filters", new { action, entityType, entityId, actorUserId, branchId, outcome = outcome?.ToString(), search }).ToJson()), ct);

            var csv = new System.Text.StringBuilder("occurred_at,action,outcome,entity_type,entity_id,actor_user_id,actor_name,branch_id,ip_address,correlation_id,details,hash\n");
            foreach (var a in rows)
                csv.Append(string.Join(',', new[] { a.OccurredAt.ToString("O"), a.Action, a.Outcome.ToString(), a.EntityType, a.EntityId, a.ActorUserId.ToString(),
                    a.ActorName, a.BranchId?.ToString(), a.IpAddress, a.CorrelationId, a.Details, a.Hash }.Select(Csv))).Append('\n');
            http.Response.Headers.ContentDisposition = $"attachment; filename=\"audit-log-{DateTime.UtcNow:yyyy-MM-dd}.csv\"";
            return Results.Text(csv.ToString(), "text/csv");
        }).RequirePermission(Permissions.Admin.AuditView).WithName("ExportAuditLog");

        // Proves the trail hasn't been edited: every row's hash is recomputed and checked against the row before it.
        admin.MapGet("/audit-log/verify", async (AuditVerifier verifier, string? from, string? to, CancellationToken ct) =>
            TypedResults.Ok(await verifier.VerifyAsync(Instant(from, "from"), Instant(to, "to"), ct)))
            .RequirePermission(Permissions.Admin.AuditView).WithName("VerifyAuditLog");
    }

    /// <summary>A filter left empty by a form ("Any outcome", a cleared date box) means no filter, not a bad request.</summary>
    private static AuditOutcome? Outcome(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
        : Enum.TryParse<AuditOutcome>(value, ignoreCase: true, out var parsed) ? parsed
        : throw new Sacco.Shared.Domain.DomainRuleException("platform.audit.outcome_invalid", "Outcome must be Success, Failure or Denied.");

    private static DateTimeOffset? Instant(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? null
        : DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) ? parsed
        : throw new Sacco.Shared.Domain.DomainRuleException("platform.audit.date_invalid", $"'{name}' isn't a date and time the server understands.");

    private static IQueryable<AuditLogEntry> AuditQuery(PlatformDbContext db, string? action, string? entityType, string? entityId, Guid? actorUserId, Guid? branchId,
        AuditOutcome? outcome, DateTimeOffset? from, DateTimeOffset? to, string? search)
    {
        var q = db.AuditLog.AsNoTracking();
        // "savings." matches every savings action; an exact action still matches itself.
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action.StartsWith(action));
        if (!string.IsNullOrWhiteSpace(entityType)) q = q.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(entityId)) q = q.Where(a => a.EntityId == entityId);
        if (actorUserId is Guid actor) q = q.Where(a => a.ActorUserId == actor);
        if (branchId is Guid branch) q = q.Where(a => a.BranchId == branch);
        if (outcome is AuditOutcome o) q = q.Where(a => a.Outcome == o);
        if (from is DateTimeOffset f) q = q.Where(a => a.OccurredAt >= f);
        if (to is DateTimeOffset t) q = q.Where(a => a.OccurredAt <= t);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            q = q.Where(a => EF.Functions.ILike(a.EntityId, pattern) || EF.Functions.ILike(a.Action, pattern)
                || (a.ActorName != null && EF.Functions.ILike(a.ActorName, pattern))
                || (a.Details != null && EF.Functions.ILike(a.Details, pattern)));
        }
        return q;
    }

    private static string Csv(string? value) =>
        value is null ? "" : $"\"{value.Replace("\"", "\"\"")}\"";

    private static AuditLogResponse ToResponse(AuditLogEntry a) =>
        new(a.Id, a.OccurredAt, a.Action, a.EntityType, a.EntityId, a.ActorUserId, a.Details, a.CorrelationId,
            a.ActorName, a.Outcome, a.IpAddress, a.UserAgent, a.BranchId, a.Hash);

    private static BranchResponse ToResponse(Branch b) =>
        new(b.Id, b.Code, b.Name, b.IsHeadOffice, b.County, b.Town, b.PhysicalAddress, b.PhoneNumber, b.Email, b.DisplayOrder, b.IsActive);
}
