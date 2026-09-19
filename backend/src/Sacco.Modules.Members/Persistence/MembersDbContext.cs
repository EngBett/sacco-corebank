using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Members.Domain;
using Sacco.Shared.Persistence;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Members.Persistence;

public class MembersDbContext(DbContextOptions<MembersDbContext> options, ITenantContext tenant) : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "members";
    public override string Schema => SchemaName;

    public DbSet<Member> Members => Set<Member>();
    public DbSet<MembershipApplication> Applications => Set<MembershipApplication>();
    public DbSet<MemberNumberSequence> Sequences => Set<MemberNumberSequence>();

    private static void MapDetails<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.OwnedNavigationBuilder<T, PersonalDetails> d) where T : class
    {
        d.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        d.Property(x => x.MiddleName).HasMaxLength(100);
        d.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        d.Property(x => x.NationalIdNumber).HasMaxLength(20).IsRequired();
        d.Property(x => x.KraPin).HasMaxLength(20);
        d.Property(x => x.PhoneNumber).HasMaxLength(15).IsRequired();
        d.Property(x => x.Email).HasMaxLength(200);
        d.Property(x => x.PostalAddress).HasMaxLength(200);
        d.Property(x => x.County).HasMaxLength(50).IsRequired();
        d.Property(x => x.Occupation).HasMaxLength(100);
        d.Property(x => x.Employer).HasMaxLength(150);
        d.Property(x => x.EmployeeNumber).HasMaxLength(50);
        d.Property(x => x.Gender).HasConversion<int>();
        d.Ignore(x => x.FullName);
    }

    private static void MapKin<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.OwnedNavigationBuilder<T, NextOfKin> k) where T : class
    {
        k.Property(x => x.Name).HasMaxLength(150);
        k.Property(x => x.Relationship).HasMaxLength(50);
        k.Property(x => x.PhoneNumber).HasMaxLength(15);
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Member>(b =>
        {
            b.ToTable("members");
            b.HasKey(m => m.Id);
            b.Property(m => m.MemberNumber).HasMaxLength(20).IsRequired();
            b.HasIndex(m => new { m.TenantId, m.MemberNumber }).IsUnique();
            b.HasIndex(m => new { m.TenantId, m.BranchId });
            b.OwnsOne(m => m.Details, d =>
            {
                MapDetails(d);
                d.HasIndex(x => x.NationalIdNumber);
                d.HasIndex(x => x.PhoneNumber);
            });
            b.OwnsOne(m => m.NextOfKin, MapKin);
            b.Property(m => m.KycStatus).HasConversion<int>();
            b.Property(m => m.Source).HasConversion<int>();
            b.Property(m => m.KycRejectionReason).HasMaxLength(500);
            b.Property(m => m.SuspensionReason).HasMaxLength(500);
            b.Property(m => m.ExitReason).HasMaxLength(500);
            b.Property(m => m.ExitSettlementJson).HasColumnType("jsonb");
            b.HasIndex(m => new { m.TenantId, m.KycStatus });
            b.HasMany(m => m.Documents).WithOne().HasForeignKey(d => d.MemberId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(m => m.Documents).AutoInclude();
            b.Ignore(m => m.IsInGoodStanding);
        });

        mb.Entity<KycDocument>(b =>
        {
            b.ToTable("kyc_documents");
            b.HasKey(d => d.Id);
            b.Property(d => d.FileReference).HasMaxLength(500).IsRequired();
            b.Property(d => d.Type).HasConversion<int>();
        });

        mb.Entity<MembershipApplication>(b =>
        {
            b.ToTable("membership_applications");
            b.HasKey(a => a.Id);
            b.OwnsOne(a => a.Details, d => { MapDetails(d); d.HasIndex(x => x.NationalIdNumber); });
            b.OwnsOne(a => a.NextOfKin, MapKin);
            b.Property(a => a.Status).HasConversion<int>();
            b.Property(a => a.Channel).HasMaxLength(30);
            b.Property(a => a.SourceFingerprint).HasMaxLength(64);
            b.Property(a => a.ReviewNotes).HasMaxLength(500);
            b.HasIndex(a => new { a.TenantId, a.Status, a.SubmittedAt });
        });

        mb.Entity<MemberNumberSequence>(b =>
        {
            b.ToTable("member_number_sequences");
            b.HasKey(s => s.TenantId);
        });

        base.OnModelCreating(mb);
    }
}
