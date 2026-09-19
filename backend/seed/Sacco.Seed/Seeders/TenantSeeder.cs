using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sacco.Modules.Platform.Domain;
using Sacco.Modules.Platform.Persistence;
using Sacco.Seed.Data;
using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Seed.Seeders;

public sealed class TenantSeeder(PlatformDbContext db, TenantContext tenant, IClock clock, ILogger<TenantSeeder> logger) : ISeeder
{
    public int Order => 0;

    /// <summary>The name the demo tenant was seeded with before it became Icodeio SACCO.</summary>
    private const string LegacyName = "Demo SACCO Society Ltd";

    public async Task SeedAsync(CancellationToken ct)
    {
        var existing = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == DemoTenant.Slug, ct);
        if (existing is null)
        {
            var t = Tenant.Create(DemoTenant.Id, DemoTenant.Slug, DemoTenant.Name, DemoTenant.ShortName, Branding(), "DT/SASRA/0000-DEMO", clock.UtcNow);
            db.Tenants.Add(t);
            await db.SaveChangesAsync(ct);
            logger.Created("Tenant", 1);
        }
        else if (existing.Name == LegacyName)
        {
            // Databases seeded before the rename pick up the new name and branding on the next run, without a --reset.
            // Anything an admin has since changed through the portal (a different name) is left alone.
            existing.UpdateDetails(DemoTenant.Name, DemoTenant.ShortName, existing.SasraLicenceNumber, existing.CustomDomain);
            existing.UpdateBranding(Branding());
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Renamed tenant '{Slug}' to {Name}", DemoTenant.Slug, DemoTenant.Name);
        }
        else if (existing.Branding.FaviconUrl is null || existing.Branding.LightModeLogoUrl is null || existing.Branding.DarkModeLogoUrl is null)
        {
            // Tenants seeded before these assets existed get the defaults; anything an admin has set is kept.
            existing.UpdateBranding(new TenantBranding
            {
                PrimaryColor = existing.Branding.PrimaryColor,
                SecondaryColor = existing.Branding.SecondaryColor,
                AccentColor = existing.Branding.AccentColor,
                LogoUrl = existing.Branding.LogoUrl,
                FaviconUrl = existing.Branding.FaviconUrl ?? DemoTenant.FaviconUrl,
                LightModeLogoUrl = existing.Branding.LightModeLogoUrl ?? DemoTenant.LightModeLogoUrl,
                DarkModeLogoUrl = existing.Branding.DarkModeLogoUrl ?? DemoTenant.DarkModeLogoUrl,
                Tagline = existing.Branding.Tagline,
                SupportEmail = existing.Branding.SupportEmail,
                SupportPhone = existing.Branding.SupportPhone,
            });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Added default branding assets (favicon, light/dark-mode logos) to tenant '{Slug}'", DemoTenant.Slug);
        }
        tenant.Set(DemoTenant.Id, DemoTenant.Slug);

        if (!await db.Branches.AnyAsync(ct))
        {
            var order = 0;
            foreach (var (code, name, headOffice, county, town, address, phone) in DemoTenant.Branches)
            {
                var branch = Branch.Create(DemoTenant.BranchId(code), DemoTenant.Id, code, name, headOffice, clock.UtcNow);
                branch.Update(code, name, headOffice, county, town, address, phone, $"{code.ToLowerInvariant()}@icodeio.example.co.ke", order += 10, isActive: true);
                db.Branches.Add(branch);
            }
            await db.SaveChangesAsync(ct);
            logger.Created("Branches", DemoTenant.Branches.Length);
        }

        if (!await db.PublicServices.AnyAsync(ct))
        {
            var order = 0;
            foreach (var (name, description, icon) in PublicCatalogue.Services)
                db.PublicServices.Add(PublicService.Create(Ids.Deterministic($"public-service:{DemoTenant.Slug}:{name}"), DemoTenant.Id, name, description, icon, order += 10));
            await db.SaveChangesAsync(ct);
            logger.Created("Public website services", PublicCatalogue.Services.Count);
        }
    }

    private static TenantBranding Branding() => new()
    {
        PrimaryColor = DemoTenant.PrimaryColor,
        SecondaryColor = "#f59e0b",
        AccentColor = "#1d4ed8",
        // Served by the API from wwwroot; clients resolve it against the API origin they already call.
        LogoUrl = DemoTenant.LogoUrl,
        FaviconUrl = DemoTenant.FaviconUrl,
        LightModeLogoUrl = DemoTenant.LightModeLogoUrl,
        DarkModeLogoUrl = DemoTenant.DarkModeLogoUrl,
        Tagline = "Growing together, one shilling at a time",
        SupportEmail = "care@icodeio.example.co.ke",
        SupportPhone = "254700100000",
    };
}
