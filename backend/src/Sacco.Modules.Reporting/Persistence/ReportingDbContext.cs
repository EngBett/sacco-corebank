using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Reporting.Domain;
using Sacco.Shared.Persistence;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Reporting.Persistence;

public class ReportingDbContext(DbContextOptions<ReportingDbContext> options, ITenantContext tenant) : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "reporting";
    public override string Schema => SchemaName;
    public DbSet<StatutoryReturn> Returns => Set<StatutoryReturn>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<StatutoryReturn>(b =>
        {
            b.ToTable("statutory_returns");
            b.HasKey(r => r.Id);
            b.HasIndex(r => new { r.TenantId, r.PeriodEnd });
            b.Property(r => r.Package).HasColumnType("jsonb");
            b.Property(r => r.ReconciliationNotes).HasMaxLength(2000);
            b.Property(r => r.SubmissionReference).HasMaxLength(100);
            b.Property(r => r.WithdrawalReason).HasMaxLength(500);
            b.Property(r => r.Status).HasConversion<int>();
        });
        base.OnModelCreating(mb);
    }
}
