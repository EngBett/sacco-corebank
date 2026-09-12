using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Sacco.Shared.Tenancy;

namespace Sacco.Shared.Persistence;

/// <summary>
/// Base for every module's DbContext. Each module owns one Postgres schema and one migrations
/// history table, and every tenant-scoped entity is filtered by the ambient tenant.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options, ITenantContext tenant) : DbContext(options)
{
    private static readonly MethodInfo ConfigureTenantFilterMethod = typeof(ModuleDbContext)
        .GetMethod(nameof(ConfigureTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    protected ITenantContext Tenant { get; } = tenant;

    public abstract string Schema { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (!typeof(TenantEntity).IsAssignableFrom(entityType.ClrType)) continue;
            ConfigureTenantFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }

        base.OnModelCreating(modelBuilder);
    }

    private void ConfigureTenantFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : TenantEntity
    {
        // Closure over `this` — EF Core re-evaluates the context member per query, so the cached
        // model is safe across context instances.
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == CurrentTenantIdOrEmpty);
        modelBuilder.Entity<TEntity>().HasIndex(e => e.TenantId);
    }

    /// <summary>Used by the query filter; an unresolved tenant matches no rows rather than all rows.</summary>
    public Guid CurrentTenantIdOrEmpty => Tenant.HasTenant ? Tenant.TenantId : Guid.Empty;

    /// <summary>Guards writes: every tenant entity being added must belong to the ambient tenant.</summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AssertTenantOnAddedEntities();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AssertTenantOnAddedEntities();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void AssertTenantOnAddedEntities()
    {
        foreach (var entry in ChangeTracker.Entries<TenantEntity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            if (!Tenant.HasTenant)
                throw new InvalidOperationException($"Cannot save {entry.Metadata.ClrType.Name} without a resolved tenant.");
            if (entry.Entity.TenantId != Tenant.TenantId)
                throw new InvalidOperationException(
                    $"{entry.Metadata.ClrType.Name} belongs to tenant {entry.Entity.TenantId} but the current tenant is {Tenant.TenantId}.");
        }
    }
}
