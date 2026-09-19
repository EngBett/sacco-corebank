using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Identity.Domain;
using Sacco.Shared.Persistence;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Identity.Persistence;

public class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ITenantContext tenant) : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "identity";
    public override string Schema => SchemaName;

    public DbSet<StaffUser> Users => Set<StaffUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<PersistedGrantRecord> PersistedGrants => Set<PersistedGrantRecord>();
    public DbSet<MemberLogin> MemberLogins => Set<MemberLogin>();
    public DbSet<MemberTrustedDevice> MemberTrustedDevices => Set<MemberTrustedDevice>();
    public DbSet<MemberOtpChallenge> MemberOtpChallenges => Set<MemberOtpChallenge>();
    public DbSet<StaffInvitation> StaffInvitations => Set<StaffInvitation>();
    public DbSet<StaffAccountToken> StaffAccountTokens => Set<StaffAccountToken>();
    public DbSet<StaffRecoveryCode> StaffRecoveryCodes => Set<StaffRecoveryCode>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<StaffUser>(b =>
        {
            b.ToTable("users");
            b.HasKey(u => u.Id);
            b.Property(u => u.UserName).HasMaxLength(100).IsRequired();
            b.HasIndex(u => new { u.TenantId, u.UserName }).IsUnique();
            b.Property(u => u.Email).HasMaxLength(200).IsRequired();
            b.Property(u => u.DisplayName).HasMaxLength(150).IsRequired();
            b.Property(u => u.PhoneNumber).HasMaxLength(20);
            b.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
            b.Property(u => u.TotpSecret).HasMaxLength(200);
            b.Property(u => u.TotpPendingSecret).HasMaxLength(200);
            b.Ignore(u => u.Status);
            b.Ignore(u => u.IsActivated);
            b.Ignore(u => u.IsMfaEnabled);
            b.HasMany(u => u.Roles).WithOne().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(u => u.Roles).AutoInclude();
        });

        mb.Entity<StaffInvitation>(b =>
        {
            b.ToTable("staff_invitations");
            b.HasKey(i => i.Id);
            b.Property(i => i.UserName).HasMaxLength(100).IsRequired();
            b.Property(i => i.Email).HasMaxLength(200).IsRequired();
            b.Property(i => i.DisplayName).HasMaxLength(150).IsRequired();
            b.Property(i => i.PhoneNumber).HasMaxLength(20);
            b.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(i => i.RejectionReason).HasMaxLength(500);
            b.HasIndex(i => new { i.TenantId, i.Status });
        });

        mb.Entity<StaffAccountToken>(b =>
        {
            b.ToTable("staff_account_tokens");
            b.HasKey(t => t.Id);
            b.Property(t => t.Purpose).HasConversion<string>().HasMaxLength(20);
            b.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            b.HasIndex(t => t.TokenHash).IsUnique();
            b.HasIndex(t => new { t.TenantId, t.UserId, t.Purpose });
        });

        mb.Entity<StaffRecoveryCode>(b =>
        {
            b.ToTable("staff_recovery_codes");
            b.HasKey(c => c.Id);
            b.Property(c => c.CodeHash).HasMaxLength(64).IsRequired();
            b.HasIndex(c => new { c.TenantId, c.UserId });
        });

        mb.Entity<UserRole>(b =>
        {
            b.ToTable("user_roles");
            b.HasKey(r => new { r.UserId, r.RoleId });
            b.HasOne<Role>().WithMany().HasForeignKey(r => r.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<Role>(b =>
        {
            b.ToTable("roles");
            b.HasKey(r => r.Id);
            b.Property(r => r.Name).HasMaxLength(100).IsRequired();
            b.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();
            b.Property(r => r.Description).HasMaxLength(300);
            b.HasMany(r => r.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(r => r.Permissions).AutoInclude();
        });

        mb.Entity<RolePermission>(b =>
        {
            b.ToTable("role_permissions");
            b.HasKey(p => new { p.RoleId, p.Permission });
            b.Property(p => p.Permission).HasMaxLength(100);
        });

        mb.Entity<MemberLogin>(b =>
        {
            b.ToTable("member_logins");
            b.HasKey(l => l.Id);
            b.HasIndex(l => new { l.TenantId, l.MemberId }).IsUnique();
            b.HasIndex(l => new { l.TenantId, l.PhoneNumber });
            b.Property(l => l.PhoneNumber).HasMaxLength(20).IsRequired();
            b.Property(l => l.DisplayName).HasMaxLength(150).IsRequired();
            b.Property(l => l.PinHash).HasMaxLength(500).IsRequired();
        });

        mb.Entity<MemberTrustedDevice>(b =>
        {
            b.ToTable("member_trusted_devices");
            b.HasKey(d => d.Id);
            b.HasIndex(d => new { d.TenantId, d.MemberId, d.DeviceId }).IsUnique();
            b.Property(d => d.DeviceId).HasMaxLength(200).IsRequired();
            b.Property(d => d.DeviceName).HasMaxLength(150);
            b.Property(d => d.Platform).HasMaxLength(20).IsRequired();
        });

        mb.Entity<MemberOtpChallenge>(b =>
        {
            b.ToTable("member_otp_challenges");
            b.HasKey(c => c.Id);
            b.HasIndex(c => new { c.TenantId, c.MemberId, c.DeviceId });
            b.Property(c => c.DeviceId).HasMaxLength(200).IsRequired();
            b.Property(c => c.PhoneNumber).HasMaxLength(20).IsRequired();
            b.Property(c => c.CodeHash).HasMaxLength(500).IsRequired();
        });

        mb.Entity<PersistedGrantRecord>(b =>
        {
            b.ToTable("persisted_grants");
            b.HasKey(g => g.Key);
            b.Property(g => g.Key).HasMaxLength(200);
            b.Property(g => g.Type).HasMaxLength(50).IsRequired();
            b.Property(g => g.SubjectId).HasMaxLength(200);
            b.Property(g => g.SessionId).HasMaxLength(100);
            b.Property(g => g.ClientId).HasMaxLength(200).IsRequired();
            b.Property(g => g.Description).HasMaxLength(200);
            b.HasIndex(g => new { g.SubjectId, g.ClientId, g.Type });
            b.HasIndex(g => g.Expiration);
        });

        base.OnModelCreating(mb);
    }
}
