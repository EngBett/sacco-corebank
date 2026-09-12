using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Lending.Domain;
using Sacco.Shared.Persistence;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Lending.Persistence;

public class LendingDbContext(DbContextOptions<LendingDbContext> options, ITenantContext tenant) : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "lending";
    public override string Schema => SchemaName;

    public DbSet<LoanProduct> Products => Set<LoanProduct>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<ProvisioningConfig> ProvisioningConfigs => Set<ProvisioningConfig>();
    public DbSet<ProvisioningRun> ProvisioningRuns => Set<ProvisioningRun>();
    public DbSet<LoanNumberSequence> Sequences => Set<LoanNumberSequence>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        const string money = "numeric(18,2)";
        mb.Entity<LoanProduct>(b =>
        {
            b.ToTable("products");
            b.HasKey(p => p.Id);
            b.Property(p => p.Code).HasMaxLength(30).IsRequired();
            b.HasIndex(p => new { p.TenantId, p.Code }).IsUnique();
            b.Property(p => p.Name).HasMaxLength(150).IsRequired();
            b.Property(p => p.Description).HasMaxLength(500);
            foreach (var gl in new[] { nameof(LoanProduct.ControlGlAccountCode), nameof(LoanProduct.InterestIncomeGlAccountCode), nameof(LoanProduct.InterestReceivableGlAccountCode), nameof(LoanProduct.FeeIncomeGlAccountCode), nameof(LoanProduct.ProvisionGlAccountCode), nameof(LoanProduct.ProvisionExpenseGlAccountCode) })
                b.Property(gl).HasMaxLength(20).IsRequired();
            foreach (var m in new[] { nameof(LoanProduct.MinAmount), nameof(LoanProduct.MaxAmount), nameof(LoanProduct.CommitteeThreshold) }) b.Property(m).HasColumnType(money);
            b.Property(p => p.DepositMultiplier).HasColumnType("numeric(6,2)");
            b.Property(p => p.Segment).HasConversion<int>();
            b.Property(p => p.InterestMethod).HasConversion<int>();
        });

        mb.Entity<Loan>(b =>
        {
            b.ToTable("loans");
            b.HasKey(l => l.Id);
            b.Property(l => l.LoanNumber).HasMaxLength(20).IsRequired();
            b.HasIndex(l => new { l.TenantId, l.LoanNumber }).IsUnique();
            b.HasIndex(l => new { l.TenantId, l.MemberId });
            b.HasIndex(l => new { l.TenantId, l.Status });
            b.Property(l => l.ProductCode).HasMaxLength(30).IsRequired();
            b.Property(l => l.Purpose).HasMaxLength(300).IsRequired();
            b.Property(l => l.DisbursementAccountNumber).HasMaxLength(30).IsRequired();
            b.Property(l => l.PledgedDepositsAccountNumber).HasMaxLength(30);
            b.Property(l => l.LedgerAccountNumber).HasMaxLength(30);
            b.Property(l => l.AppraisalNotes).HasMaxLength(1000);
            b.Property(l => l.RejectionReason).HasMaxLength(500);
            foreach (var m in new[] { nameof(Loan.Amount), nameof(Loan.PledgedDepositsAmount), nameof(Loan.ProcessingFee) }) b.Property(m).HasColumnType(money);
            b.Property(l => l.Segment).HasConversion<int>();
            b.Property(l => l.InterestMethod).HasConversion<int>();
            b.Property(l => l.Status).HasConversion<int>();
            b.OwnsOne(l => l.Eligibility, e =>
            {
                foreach (var m in new[] { nameof(EligibilitySnapshot.BosaDeposits), nameof(EligibilitySnapshot.Shares), nameof(EligibilitySnapshot.MaxEligibleAmount), nameof(EligibilitySnapshot.ExistingOutstanding) }) e.Property(m).HasColumnType(money);
                e.Property(x => x.DepositMultiplier).HasColumnType("numeric(6,2)");
            });
            b.HasOne<LoanProduct>().WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(l => l.Guarantors).WithOne().HasForeignKey(g => g.LoanId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(l => l.Approvals).WithOne().HasForeignKey(a => a.LoanId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(l => l.Schedule).WithOne().HasForeignKey(s => s.LoanId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(l => l.Guarantors).AutoInclude();
            b.Navigation(l => l.Approvals).AutoInclude();
            b.Navigation(l => l.Schedule).AutoInclude();
            b.Ignore(l => l.AcceptedGuarantees);
            b.Ignore(l => l.IsFullyRepaid);
        });

        mb.Entity<LoanGuarantor>(b =>
        {
            b.ToTable("loan_guarantors");
            b.HasKey(g => g.Id);
            b.HasIndex(g => g.GuarantorMemberId);
            b.Property(g => g.DepositsAccountNumber).HasMaxLength(30).IsRequired();
            b.Property(g => g.AmountGuaranteed).HasColumnType(money);
            b.Property(g => g.Status).HasConversion<int>();
        });

        mb.Entity<LoanApproval>(b =>
        {
            b.ToTable("loan_approvals");
            b.HasKey(a => a.Id);
            b.HasIndex(a => new { a.LoanId, a.ApproverUserId }).IsUnique();
            b.Property(a => a.Notes).HasMaxLength(500);
            b.Property(a => a.Decision).HasConversion<int>();
        });

        mb.Entity<RepaymentInstallment>(b =>
        {
            b.ToTable("repayment_schedule");
            b.HasKey(i => i.Id);
            b.HasIndex(i => new { i.LoanId, i.Number }).IsUnique();
            b.HasIndex(i => new { i.DueDate, i.Status });
            foreach (var m in new[] { nameof(RepaymentInstallment.PrincipalDue), nameof(RepaymentInstallment.InterestDue), nameof(RepaymentInstallment.PrincipalPaid), nameof(RepaymentInstallment.InterestPaid) }) b.Property(m).HasColumnType(money);
            b.Property(i => i.Status).HasConversion<int>();
            b.Ignore(i => i.TotalDue);
            b.Ignore(i => i.Outstanding);
        });

        mb.Entity<ProvisioningConfig>(b =>
        {
            b.ToTable("provisioning_config");
            b.HasKey(c => c.Id);
            b.HasIndex(c => c.TenantId).IsUnique();
            b.Property(c => c.Source).HasMaxLength(300);
            b.HasMany(c => c.Buckets).WithOne().HasForeignKey(x => x.ConfigId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(c => c.Buckets).AutoInclude();
        });

        mb.Entity<AgingBucket>(b =>
        {
            b.ToTable("aging_buckets");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(50).IsRequired();
        });

        mb.Entity<ProvisioningRun>(b =>
        {
            b.ToTable("provisioning_runs");
            b.HasKey(r => r.Id);
            b.HasIndex(r => new { r.TenantId, r.AsOf });
            b.Property(r => r.RejectionReason).HasMaxLength(500);
            b.Property(r => r.TotalOutstanding).HasColumnType(money);
            b.Property(r => r.TotalProvisionRequired).HasColumnType(money);
            b.Property(r => r.Status).HasConversion<int>();
            b.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.RunId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(r => r.Lines).AutoInclude();
        });

        mb.Entity<ProvisioningLine>(b =>
        {
            b.ToTable("provisioning_lines");
            b.HasKey(l => l.Id);
            b.Property(l => l.LoanNumber).HasMaxLength(20).IsRequired();
            b.Property(l => l.ProductCode).HasMaxLength(30).IsRequired();
            b.Property(l => l.Bucket).HasMaxLength(50).IsRequired();
            foreach (var m in new[] { nameof(ProvisioningLine.OutstandingPrincipal), nameof(ProvisioningLine.ArrearsAmount), nameof(ProvisioningLine.ProvisionRequired) }) b.Property(m).HasColumnType(money);
            b.Property(l => l.Segment).HasConversion<int>();
        });

        mb.Entity<LoanNumberSequence>(b =>
        {
            b.ToTable("loan_number_sequences");
            b.HasKey(s => s.TenantId);
        });

        base.OnModelCreating(mb);
    }
}
