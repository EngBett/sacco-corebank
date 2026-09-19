using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Ledger.Domain;
using Sacco.Shared.Persistence;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Ledger.Persistence;

public class LedgerDbContext(DbContextOptions<LedgerDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "ledger";
    public override string Schema => SchemaName;

    public DbSet<GlAccount> GlAccounts => Set<GlAccount>();
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<GlAccount>(b =>
        {
            b.ToTable("gl_accounts", t => t.HasCheckConstraint("ck_gl_accounts_segment", "segment IN (1, 2)"));
            b.HasKey(a => a.Id);
            b.Property(a => a.Code).HasMaxLength(20).IsRequired();
            b.HasIndex(a => new { a.TenantId, a.Code }).IsUnique();
            b.Property(a => a.Name).HasMaxLength(150).IsRequired();
            b.Property(a => a.Description).HasMaxLength(500);
            b.Property(a => a.Balance).HasColumnType("numeric(18,2)");
            b.HasOne<GlAccount>().WithMany().HasForeignKey(a => a.ParentId).OnDelete(DeleteBehavior.Restrict);
            b.Property(a => a.Category).HasConversion<int>();
            b.Property(a => a.Segment).HasConversion<int>();
            b.Property(a => a.NormalBalance).HasConversion<int>();
        });

        mb.Entity<LedgerAccount>(b =>
        {
            b.ToTable("ledger_accounts", t =>
            {
                t.HasCheckConstraint("ck_ledger_accounts_segment", "segment IN (1, 2)");
                t.HasCheckConstraint("ck_ledger_accounts_held_non_negative", "held_amount >= 0");
            });
            b.HasKey(a => a.Id);
            b.Property(a => a.AccountNumber).HasMaxLength(30).IsRequired();
            b.HasIndex(a => new { a.TenantId, a.AccountNumber }).IsUnique();
            b.HasIndex(a => new { a.TenantId, a.MemberId });
            b.Property(a => a.ProductCode).HasMaxLength(30).IsRequired();
            b.Property(a => a.Currency).HasMaxLength(3).IsRequired();
            b.Property(a => a.Balance).HasColumnType("numeric(18,2)");
            b.Property(a => a.HeldAmount).HasColumnType("numeric(18,2)");
            b.Ignore(a => a.AvailableBalance);
            b.HasOne<GlAccount>().WithMany().HasForeignKey(a => a.ControlGlAccountId).OnDelete(DeleteBehavior.Restrict);
            b.Property(a => a.Segment).HasConversion<int>();
            b.Property(a => a.Kind).HasConversion<int>();
            b.Property(a => a.Status).HasConversion<int>();
        });

        mb.Entity<JournalEntry>(b =>
        {
            b.ToTable("journal_entries");
            b.HasKey(e => e.Id);
            b.Property(e => e.Reference).HasMaxLength(100).IsRequired();
            b.HasIndex(e => new { e.TenantId, e.Reference }).IsUnique();
            b.HasIndex(e => new { e.TenantId, e.ValueDate });
            b.HasIndex(e => new { e.TenantId, e.Status });
            b.HasIndex(e => new { e.TenantId, e.BranchId });
            b.Property(e => e.Description).HasMaxLength(500).IsRequired();
            b.Property(e => e.Source).HasMaxLength(50).IsRequired();
            b.Property(e => e.RejectionReason).HasMaxLength(500);
            b.Property(e => e.TotalAmount).HasColumnType("numeric(18,2)");
            b.Property(e => e.Status).HasConversion<int>();
            b.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(e => e.Lines).AutoInclude();
            b.HasOne<JournalEntry>().WithMany().HasForeignKey(e => e.ReversalOfEntryId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<JournalLine>(b =>
        {
            b.ToTable("journal_lines", t =>
            {
                t.HasCheckConstraint("ck_journal_lines_amount_positive", "amount > 0");
                t.HasCheckConstraint("ck_journal_lines_segment", "segment IN (1, 2)");
            });
            b.HasKey(l => l.Id);
            b.HasIndex(l => new { l.JournalEntryId, l.LineNumber }).IsUnique();
            b.HasIndex(l => l.GlAccountId);
            b.HasIndex(l => l.LedgerAccountId);
            b.Property(l => l.GlAccountCode).HasMaxLength(20).IsRequired();
            b.Property(l => l.LedgerAccountNumber).HasMaxLength(30);
            b.Property(l => l.Narrative).HasMaxLength(300);
            b.Property(l => l.Amount).HasColumnType("numeric(18,2)");
            b.Property(l => l.Segment).HasConversion<int>();
            b.Property(l => l.Direction).HasConversion<int>();
            b.HasOne<GlAccount>().WithMany().HasForeignKey(l => l.GlAccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<LedgerAccount>().WithMany().HasForeignKey(l => l.LedgerAccountId).OnDelete(DeleteBehavior.Restrict);
        });

        base.OnModelCreating(mb);
    }
}
