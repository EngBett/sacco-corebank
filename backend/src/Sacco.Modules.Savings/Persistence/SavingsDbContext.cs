using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Savings.Domain;
using Sacco.Shared.Persistence;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Savings.Persistence;

public class SavingsDbContext(DbContextOptions<SavingsDbContext> options, ITenantContext tenant) : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "savings";
    public override string Schema => SchemaName;

    public DbSet<SavingsProduct> Products => Set<SavingsProduct>();
    public DbSet<SavingsAccount> Accounts => Set<SavingsAccount>();
    public DbSet<WithdrawalRequest> Withdrawals => Set<WithdrawalRequest>();
    public DbSet<DividendDeclaration> Dividends => Set<DividendDeclaration>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<SavingsProduct>(b =>
        {
            b.ToTable("products");
            b.HasKey(p => p.Id);
            b.Property(p => p.Code).HasMaxLength(30).IsRequired();
            b.HasIndex(p => new { p.TenantId, p.Code }).IsUnique();
            b.Property(p => p.Name).HasMaxLength(150).IsRequired();
            b.Property(p => p.Description).HasMaxLength(500);
            b.Property(p => p.ControlGlAccountCode).HasMaxLength(20).IsRequired();
            b.Property(p => p.AccountSuffix).HasMaxLength(4).IsRequired();
            b.Property(p => p.FeeIncomeGlAccountCode).HasMaxLength(20);
            b.Property(p => p.InterestExpenseGlAccountCode).HasMaxLength(20);
            foreach (var prop in new[] { nameof(SavingsProduct.MinimumOpeningDeposit), nameof(SavingsProduct.MinimumBalance), nameof(SavingsProduct.TellerWithdrawalLimit), nameof(SavingsProduct.WithdrawalFee) })
                b.Property(prop).HasColumnType("numeric(18,2)");
            b.Property(p => p.Kind).HasConversion<int>();
            b.Property(p => p.Segment).HasConversion<int>();
        });

        mb.Entity<SavingsAccount>(b =>
        {
            b.ToTable("accounts");
            b.HasKey(a => a.Id);
            b.Property(a => a.AccountNumber).HasMaxLength(30).IsRequired();
            b.HasIndex(a => new { a.TenantId, a.AccountNumber }).IsUnique();
            b.HasIndex(a => new { a.TenantId, a.MemberId });
            b.Property(a => a.ProductCode).HasMaxLength(30).IsRequired();
            b.Property(a => a.PayoutAccountNumber).HasMaxLength(30);
            b.Property(a => a.Principal).HasColumnType("numeric(18,2)");
            b.Property(a => a.Kind).HasConversion<int>();
            b.Property(a => a.Segment).HasConversion<int>();
            b.Property(a => a.Status).HasConversion<int>();
            b.HasOne<SavingsProduct>().WithMany().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<WithdrawalRequest>(b =>
        {
            b.ToTable("withdrawal_requests");
            b.HasKey(w => w.Id);
            b.Property(w => w.AccountNumber).HasMaxLength(30).IsRequired();
            b.Property(w => w.Amount).HasColumnType("numeric(18,2)");
            b.Property(w => w.Fee).HasColumnType("numeric(18,2)");
            b.Property(w => w.PayoutDestination).HasMaxLength(100);
            b.Property(w => w.JournalReference).HasMaxLength(100);
            b.Property(w => w.RejectionReason).HasMaxLength(500);
            b.Property(w => w.Narrative).HasMaxLength(300);
            b.Property(w => w.Channel).HasConversion<int>();
            b.Property(w => w.Status).HasConversion<int>();
            b.HasIndex(w => new { w.TenantId, w.Status });
            b.HasIndex(w => new { w.TenantId, w.AccountNumber });
            b.Ignore(w => w.TotalDebit);
        });

        mb.Entity<DividendDeclaration>(b =>
        {
            b.ToTable("dividend_declarations");
            b.HasKey(d => d.Id);
            b.HasIndex(d => new { d.TenantId, d.FinancialYear });
            b.Property(d => d.RejectionReason).HasMaxLength(500);
            foreach (var prop in new[] { nameof(DividendDeclaration.TotalShareDividend), nameof(DividendDeclaration.TotalDepositInterest), nameof(DividendDeclaration.TotalWithholdingTax) })
                b.Property(prop).HasColumnType("numeric(18,2)");
            b.Property(d => d.Status).HasConversion<int>();
            b.HasMany(d => d.Lines).WithOne().HasForeignKey(l => l.DeclarationId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(d => d.Lines).AutoInclude();
        });

        mb.Entity<DividendLine>(b =>
        {
            b.ToTable("dividend_lines");
            b.HasKey(l => l.Id);
            b.HasIndex(l => new { l.DeclarationId, l.MemberId }).IsUnique();
            b.Property(l => l.SharesAccountNumber).HasMaxLength(30);
            b.Property(l => l.DepositsAccountNumber).HasMaxLength(30);
            b.Property(l => l.PayoutAccountNumber).HasMaxLength(30);
            b.Property(l => l.JournalReference).HasMaxLength(100);
            foreach (var prop in new[] { nameof(DividendLine.ShareBalance), nameof(DividendLine.ShareDividend), nameof(DividendLine.DepositBalance), nameof(DividendLine.DepositInterest), nameof(DividendLine.WithholdingTax) })
                b.Property(prop).HasColumnType("numeric(18,2)");
            b.Ignore(l => l.NetPayable);
        });

        base.OnModelCreating(mb);
    }
}
