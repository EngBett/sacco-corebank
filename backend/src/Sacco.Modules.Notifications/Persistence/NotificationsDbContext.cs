using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Notifications.Domain;
using Sacco.Shared.Persistence;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Notifications.Persistence;

public class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, ITenantContext tenant)
    : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "notifications";
    public override string Schema => SchemaName;

    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(b =>
        {
            b.ToTable("notifications");
            b.HasKey(n => n.Id);
            b.Property(n => n.Kind).HasMaxLength(100).IsRequired();
            b.Property(n => n.Title).HasMaxLength(200).IsRequired();
            b.Property(n => n.Body).HasMaxLength(1000).IsRequired();
            b.Property(n => n.Link).HasMaxLength(500);
            b.Ignore(n => n.IsRead);
            // The bell's two queries: "unread for me" and "latest for me".
            b.HasIndex(n => new { n.TenantId, n.RecipientUserId, n.ReadAt });
            b.HasIndex(n => new { n.TenantId, n.RecipientUserId, n.CreatedAt });
        });

        base.OnModelCreating(modelBuilder);
    }
}
