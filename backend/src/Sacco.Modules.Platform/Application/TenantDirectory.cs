using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sacco.Modules.Platform.Domain;
using Sacco.Modules.Platform.Persistence;

namespace Sacco.Modules.Platform.Application;

public sealed record TenantInfo(Guid Id, string Slug, string Name, string ShortName, bool IsActive, string? CustomDomain, TenantBranding Branding);

/// <summary>Read-mostly tenant lookup used by tenant-resolution middleware. Tenants are not tenant-scoped rows, so no filter applies.</summary>
public interface ITenantDirectory
{
    Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct);
    Task<TenantInfo?> FindByHostAsync(string host, CancellationToken ct);
    Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct);
    void Invalidate(string slug);
}

public sealed class TenantDirectory(PlatformDbContext db, IMemoryCache cache) : ITenantDirectory, Sacco.Shared.Tenancy.ITenantEnumerator
{
    public async Task<IReadOnlyList<(Guid Id, string Slug)>> ListActiveAsync(CancellationToken ct)
        => (await db.Tenants.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Slug).Select(t => new { t.Id, t.Slug }).ToListAsync(ct)).Select(t => (t.Id, t.Slug)).ToList();

    public async Task<Sacco.Shared.Tenancy.TenantBrandingInfo?> GetBrandingAsync(Guid tenantId, CancellationToken ct)
    {
        var t = await FindByIdAsync(tenantId, ct);
        return t is null ? null : new(t.Name, t.ShortName, t.Branding.PrimaryColor, t.Branding.SecondaryColor, t.Branding.LogoUrl, t.Branding.SupportEmail);
    }

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public async Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct)
    {
        slug = slug.Trim().ToLowerInvariant();
        return await cache.GetOrCreateAsync($"tenant:slug:{slug}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = Ttl;
            var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug, ct);
            return t is null ? null : Map(t);
        });
    }

    public async Task<TenantInfo?> FindByHostAsync(string host, CancellationToken ct)
    {
        host = host.Trim().ToLowerInvariant();
        var byDomain = await cache.GetOrCreateAsync($"tenant:host:{host}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = Ttl;
            var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.CustomDomain == host, ct);
            return t is null ? null : Map(t);
        });
        if (byDomain is not null) return byDomain;

        // subdomain convention: <slug>.<anything>
        var firstLabel = host.Split('.')[0];
        return string.IsNullOrEmpty(firstLabel) ? null : await FindBySlugAsync(firstLabel, ct);
    }

    public async Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return t is null ? null : Map(t);
    }

    public void Invalidate(string slug) => cache.Remove($"tenant:slug:{slug.ToLowerInvariant()}");

    private static TenantInfo Map(Tenant t) => new(t.Id, t.Slug, t.Name, t.ShortName, t.IsActive, t.CustomDomain, t.Branding);
}
