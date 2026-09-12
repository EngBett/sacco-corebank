using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Payments.Domain;
using Sacco.Shared.Persistence;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Payments.Persistence;

public class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options, ITenantContext tenant) : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "payments";
    public override string Schema => SchemaName;

    public DbSet<PaymentTransaction> Transactions => Set<PaymentTransaction>();
    public DbSet<ProcessedProviderTransaction> Processed => Set<ProcessedProviderTransaction>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<PaymentTransaction>(b =>
        {
            b.ToTable("transactions");
            b.HasKey(t => t.Id);
            b.Property(t => t.Provider).HasMaxLength(40).IsRequired();
            b.Property(t => t.Counterparty).HasMaxLength(50).IsRequired();
            b.Property(t => t.OurReference).HasMaxLength(60).IsRequired();
            b.HasIndex(t => new { t.TenantId, t.OurReference }).IsUnique();
            b.Property(t => t.ProviderRequestId).HasMaxLength(100);
            b.HasIndex(t => new { t.TenantId, t.Provider, t.ProviderRequestId });
            b.Property(t => t.ProviderTransactionReference).HasMaxLength(100);
            b.Property(t => t.FailureReason).HasMaxLength(500);
            b.Property(t => t.Narrative).HasMaxLength(300);
            b.Property(t => t.Amount).HasColumnType("numeric(18,2)");
            b.Property(t => t.Kind).HasConversion<int>();
            b.Property(t => t.Status).HasConversion<int>();
            b.HasIndex(t => new { t.TenantId, t.Status });
            b.OwnsOne(t => t.Purpose, p =>
            {
                p.Property(x => x.Type).HasConversion<int>();
                p.Property(x => x.AccountNumber).HasMaxLength(30);
                p.Property(x => x.LoanNumber).HasMaxLength(20);
            });
            b.Ignore(t => t.IsFinal);
        });

        mb.Entity<ProcessedProviderTransaction>(b =>
        {
            b.ToTable("processed_provider_transactions");
            b.HasKey(p => p.Id);
            b.Property(p => p.Provider).HasMaxLength(40).IsRequired();
            b.Property(p => p.ProviderTransactionReference).HasMaxLength(100).IsRequired();
            // The idempotency guarantee. Provider references are globally unique per provider, so this is not tenant-scoped.
            b.HasIndex(p => new { p.Provider, p.ProviderTransactionReference }).IsUnique().HasDatabaseName("ux_processed_provider_transactions_reference");
            b.Property(p => p.Outcome).HasMaxLength(100).IsRequired();
        });

        base.OnModelCreating(mb);
    }
}
