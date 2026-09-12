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
            b.HasMany(u => u.Roles).WithOne().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(u => u.Roles).AutoInclude();
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
