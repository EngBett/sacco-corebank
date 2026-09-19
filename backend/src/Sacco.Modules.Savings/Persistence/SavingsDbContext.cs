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
    public DbSet<ShareListing> ShareListings => Set<ShareListing>();
    public DbSet<FeeRule> FeeRules => Set<FeeRule>();
    public DbSet<BalanceEnquiry> BalanceEnquiries => Set<BalanceEnquiry>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<SavingsProduct>(b =>
        {
            b.ToTable("products");
            b.HasKey(p => p.Id);
            b.Property(p => p.Code).HasMaxLength(30).IsRequired();
            b.HasIndex(p => new { p.TenantId, p.Code }).IsUnique();
            b.OwnsOne(p => p.Listing, l =>
            {
                l.Property(x => x.ShowOnPublicSite).HasColumnName("listing_show_on_public_site");
                l.Property(x => x.DisplayOrder).HasColumnName("listing_display_order");
                l.Property(x => x.Features).HasColumnName("listing_features");
                l.Property(x => x.Requirements).HasColumnName("listing_requirements");
                l.Property(x => x.AmountNote).HasColumnName("listing_amount_note").HasMaxLength(200);
                l.Property(x => x.ApplicationFormUrl).HasColumnName("listing_application_form_url").HasMaxLength(500);
            });
            b.Navigation(p => p.Listing).IsRequired();
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
            b.Property(w => w.FeeGlAccountCode).HasMaxLength(20);
            b.Property(w => w.FeeSegment).HasConversion<int?>();
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

        mb.Entity<ShareListing>(b =>
        {
            b.ToTable("share_listings");
            b.HasKey(l => l.Id);
            b.Property(l => l.SellerSharesAccountNumber).HasMaxLength(30).IsRequired();
            b.Property(l => l.SellerFosaAccountNumber).HasMaxLength(30).IsRequired();
            b.Property(l => l.BuyerSharesAccountNumber).HasMaxLength(30);
            b.Property(l => l.BuyerFosaAccountNumber).HasMaxLength(30);
            b.Property(l => l.Amount).HasColumnType("numeric(18,2)");
            b.Property(l => l.RejectionReason).HasMaxLength(500);
            b.Property(l => l.JournalReference).HasMaxLength(100);
            b.Property(l => l.Status).HasConversion<int>();
            b.HasIndex(l => new { l.TenantId, l.Status });
            b.HasIndex(l => new { l.TenantId, l.SellerMemberId });
            b.HasIndex(l => new { l.TenantId, l.BuyerMemberId });
        });

        mb.Entity<FeeRule>(b =>
        {
            b.ToTable("fee_rules");
            b.HasKey(r => r.Id);
            b.Property(r => r.TransactionType).HasConversion<int>();
            b.Property(r => r.Channel).HasConversion<int?>();
            b.Property(r => r.ChargeType).HasConversion<int>();
            b.Property(r => r.Status).HasConversion<int>();
            b.Property(r => r.FeeIncomeSegment).HasConversion<int>();
            b.Property(r => r.ProductCode).HasMaxLength(30);
            b.Property(r => r.FeeIncomeGlAccountCode).HasMaxLength(20).IsRequired();
            b.Property(r => r.RejectionReason).HasMaxLength(500);
            foreach (var prop in new[] { nameof(FeeRule.MinAmount), nameof(FeeRule.MaxAmount), nameof(FeeRule.FixedAmount), nameof(FeeRule.MinCharge), nameof(FeeRule.MaxCharge) })
                b.Property(prop).HasColumnType("numeric(18,2)");
            b.HasIndex(r => new { r.TenantId, r.Status, r.TransactionType });
            b.HasMany(r => r.Tiers).WithOne().HasForeignKey(t => t.RuleId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(r => r.Tiers).AutoInclude();
        });

        mb.Entity<FeeTier>(b =>
        {
            b.ToTable("fee_tiers");
            b.HasKey(t => t.Id);
            b.Property(t => t.UpTo).HasColumnType("numeric(18,2)");
            b.Property(t => t.Charge).HasColumnType("numeric(18,2)");
        });

        mb.Entity<BalanceEnquiry>(b =>
        {
            b.ToTable("balance_enquiries");
            b.HasKey(e => e.Id);
            b.Property(e => e.AccountNumber).HasMaxLength(30).IsRequired();
            b.Property(e => e.ChargedAccountNumber).HasMaxLength(30).IsRequired();
            b.Property(e => e.IdempotencyKey).HasMaxLength(64).IsRequired();
            b.Property(e => e.JournalReference).HasMaxLength(100);
            b.Property(e => e.Fee).HasColumnType("numeric(18,2)");
            b.HasIndex(e => new { e.TenantId, e.MemberId, e.IdempotencyKey }).IsUnique();
            b.HasIndex(e => new { e.TenantId, e.MemberId, e.VisibleUntil });
        });

        base.OnModelCreating(mb);
    }
}
