using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sacco.Modules.Platform.Domain;
using Sacco.Modules.Platform.Persistence;
using Sacco.Seed.Data;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Seed.Seeders;

public sealed class TenantSeeder(PlatformDbContext db, TenantContext tenant, IClock clock, ILogger<TenantSeeder> logger) : ISeeder
{
    public int Order => 0;

    public async Task SeedAsync(CancellationToken ct)
    {
        var existing = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == DemoTenant.Slug, ct);
        if (existing is null)
        {
            var t = Tenant.Create(DemoTenant.Id, DemoTenant.Slug, DemoTenant.Name, DemoTenant.ShortName, new TenantBranding
            {
                PrimaryColor = "#0f766e",
                SecondaryColor = "#f59e0b",
                AccentColor = "#1d4ed8",
                LogoUrl = null,
                Tagline = "Growing together, one shilling at a time",
                SupportEmail = "care@demosacco.example.co.ke",
                SupportPhone = "254700100000",
            }, "DT/SASRA/0000-DEMO", clock.UtcNow);
            db.Tenants.Add(t);
            await db.SaveChangesAsync(ct);
            logger.Created("Tenant", 1);
        }
        tenant.Set(DemoTenant.Id, DemoTenant.Slug);
    }
}
