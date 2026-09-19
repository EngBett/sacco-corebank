using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Platform.Domain;
using Sacco.Shared.Persistence;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Platform.Persistence;

public class PlatformDbContext(DbContextOptions<PlatformDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "platform";
    public override string Schema => SchemaName;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<PublicService> PublicServices => Set<PublicService>();
    public DbSet<Branch> Branches => Set<Branch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("tenants");
            b.HasKey(t => t.Id);
            b.Property(t => t.Slug).HasMaxLength(63).IsRequired();
            b.HasIndex(t => t.Slug).IsUnique();
            b.Property(t => t.Name).HasMaxLength(200).IsRequired();
            b.Property(t => t.ShortName).HasMaxLength(50).IsRequired();
            b.Property(t => t.SasraLicenceNumber).HasMaxLength(50);
            b.Property(t => t.CustomDomain).HasMaxLength(253);
            b.HasIndex(t => t.CustomDomain).IsUnique();
            b.OwnsOne(t => t.Branding, br =>
            {
                br.Property(x => x.PrimaryColor).HasMaxLength(9).IsRequired();
                br.Property(x => x.SecondaryColor).HasMaxLength(9).IsRequired();
                br.Property(x => x.AccentColor).HasMaxLength(9).IsRequired();
                br.Property(x => x.LogoUrl).HasMaxLength(500);
                br.Property(x => x.FaviconUrl).HasMaxLength(500);
                br.Property(x => x.LightModeLogoUrl).HasMaxLength(500);
                br.Property(x => x.DarkModeLogoUrl).HasMaxLength(500);
                br.Property(x => x.Tagline).HasMaxLength(200);
                br.Property(x => x.SupportEmail).HasMaxLength(200);
                br.Property(x => x.SupportPhone).HasMaxLength(20);
            });
        });

        modelBuilder.Entity<Branch>(b =>
        {
            b.ToTable("branches");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(10).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.County).HasMaxLength(100);
            b.Property(x => x.Town).HasMaxLength(100);
            b.Property(x => x.PhysicalAddress).HasMaxLength(300);
            b.Property(x => x.PhoneNumber).HasMaxLength(20);
            b.Property(x => x.Email).HasMaxLength(200);
        });

        modelBuilder.Entity<PublicService>(b =>
        {
            b.ToTable("public_services");
            b.HasKey(s => s.Id);
            b.Property(s => s.Name).HasMaxLength(100).IsRequired();
            b.Property(s => s.Description).HasMaxLength(300);
            b.Property(s => s.Icon).HasMaxLength(30).IsRequired();
            b.HasIndex(s => new { s.TenantId, s.DisplayOrder });
        });

        modelBuilder.Entity<AuditLogEntry>(b =>
        {
            b.ToTable("audit_log");
            b.HasKey(a => a.Id);
            b.Property(a => a.Action).HasMaxLength(100).IsRequired();
            b.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
            b.Property(a => a.EntityId).HasMaxLength(100).IsRequired();
            b.Property(a => a.CorrelationId).HasMaxLength(100);
            // Text, not jsonb: Postgres normalises jsonb (key order, whitespace), so a row would come back a different
            // string than the one that was hashed and every verification would report tampering. Text also lets the
            // audit search run ILIKE over the details.
            b.Property(a => a.Details).HasColumnType("text");
            b.Property(a => a.ActorName).HasMaxLength(150);
            b.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(20);
            b.Property(a => a.IpAddress).HasMaxLength(45);
            b.Property(a => a.UserAgent).HasMaxLength(300);
            b.Property(a => a.PreviousHash).HasMaxLength(64);
            b.Property(a => a.Hash).HasMaxLength(64).IsRequired();
            b.HasIndex(a => new { a.TenantId, a.Action });
            b.HasIndex(a => new { a.TenantId, a.ActorUserId });
            b.HasIndex(a => new { a.TenantId, a.BranchId });
            b.HasIndex(a => new { a.TenantId, a.OccurredAt });
            b.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId });
        });

        base.OnModelCreating(modelBuilder);
    }
}
