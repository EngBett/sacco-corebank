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
                br.Property(x => x.Tagline).HasMaxLength(200);
                br.Property(x => x.SupportEmail).HasMaxLength(200);
                br.Property(x => x.SupportPhone).HasMaxLength(20);
            });
        });

        modelBuilder.Entity<AuditLogEntry>(b =>
        {
            b.ToTable("audit_log");
            b.HasKey(a => a.Id);
            b.Property(a => a.Action).HasMaxLength(100).IsRequired();
            b.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
            b.Property(a => a.EntityId).HasMaxLength(100).IsRequired();
            b.Property(a => a.CorrelationId).HasMaxLength(100);
            b.Property(a => a.Details).HasColumnType("jsonb");
            b.HasIndex(a => new { a.TenantId, a.OccurredAt });
            b.HasIndex(a => new { a.TenantId, a.EntityType, a.EntityId });
        });

        base.OnModelCreating(modelBuilder);
    }
}
