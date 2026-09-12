using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Platform.Application;
using Sacco.Modules.Platform.Domain;
using Sacco.Modules.Platform.Persistence;
using Sacco.Shared.Auth;
using Sacco.Shared.Http;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Platform.Endpoints;

public sealed record TenantBrandingResponse(string Slug, string Name, string ShortName, string PrimaryColor, string SecondaryColor, string AccentColor, string? LogoUrl, string Tagline, string SupportEmail, string SupportPhone);
public sealed record UpdateBrandingRequest(string PrimaryColor, string SecondaryColor, string AccentColor, string? LogoUrl, string Tagline, string SupportEmail, string SupportPhone);
public sealed record AuditLogResponse(Guid Id, DateTimeOffset OccurredAt, string Action, string EntityType, string EntityId, Guid ActorUserId, string? Details, string? CorrelationId);

public sealed class PlatformEndpoints : IModuleEndpoints
{
    public void Map(IEndpointRouteBuilder app)
    {
        var pub = app.MapGroup("/api/public/tenant").WithTags("Tenant (public)");

        // Public: both Next.js apps call this server-side to theme the first paint (ADR 0006).
        pub.MapGet("/branding", (HttpContext http) =>
        {
            var t = (TenantInfo)http.Items["Tenant"]!;
            var b = t.Branding;
            return TypedResults.Ok(new TenantBrandingResponse(t.Slug, t.Name, t.ShortName, b.PrimaryColor, b.SecondaryColor, b.AccentColor, b.LogoUrl, b.Tagline, b.SupportEmail, b.SupportPhone));
        }).WithName("GetTenantBranding");

        var admin = app.MapGroup("/api/admin").WithTags("Platform admin");

        admin.MapPut("/tenant/branding", async (UpdateBrandingRequest req, PlatformDbContext db, ITenantContext tenant, ITenantDirectory directory, CancellationToken ct) =>
        {
            var t = await db.Tenants.FirstAsync(x => x.Id == tenant.TenantId, ct);
            t.UpdateBranding(new TenantBranding
            {
                PrimaryColor = req.PrimaryColor, SecondaryColor = req.SecondaryColor, AccentColor = req.AccentColor,
                LogoUrl = req.LogoUrl, Tagline = req.Tagline, SupportEmail = req.SupportEmail, SupportPhone = req.SupportPhone,
            });
            await db.SaveChangesAsync(ct);
            directory.Invalidate(t.Slug);
            return TypedResults.NoContent();
        }).RequirePermission(Permissions.Admin.TenantManage).WithName("UpdateTenantBranding");

        admin.MapGet("/audit-log", async (PlatformDbContext db, int page = 1, int pageSize = 50, string? entityType = null, string? entityId = null, CancellationToken ct = default) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 200);
            var q = db.AuditLog.AsNoTracking();
            if (!string.IsNullOrEmpty(entityType)) q = q.Where(a => a.EntityType == entityType);
            if (!string.IsNullOrEmpty(entityId)) q = q.Where(a => a.EntityId == entityId);
            var total = await q.CountAsync(ct);
            var items = await q.OrderByDescending(a => a.OccurredAt).Skip((page - 1) * pageSize).Take(pageSize)
                .Select(a => new AuditLogResponse(a.Id, a.OccurredAt, a.Action, a.EntityType, a.EntityId, a.ActorUserId, a.Details, a.CorrelationId))
                .ToListAsync(ct);
            return TypedResults.Ok(new PagedResult<AuditLogResponse>(items, page, pageSize, total));
        }).RequirePermission(Permissions.Admin.AuditView).WithName("GetAuditLog");
    }
}
