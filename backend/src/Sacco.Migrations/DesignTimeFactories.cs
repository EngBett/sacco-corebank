using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sacco.Modules.Identity.Persistence;
using Sacco.Modules.Ledger.Persistence;
using Sacco.Modules.Members.Persistence;
using Sacco.Modules.Savings.Persistence;
using Sacco.Modules.Lending.Persistence;
using Sacco.Modules.Payments.Persistence;
using Sacco.Modules.Reporting.Persistence;
using Sacco.Modules.Platform.Persistence;
using Sacco.Modules.Notifications.Persistence;
using Sacco.Shared.Persistence;
using Sacco.Shared.Tenancy;

namespace Sacco.Migrations;

/// <summary>
/// Used by `dotnet ef` only. Example:
///   dotnet ef migrations add InitialLedger --project src/Sacco.Migrations --context LedgerDbContext --output-dir Ledger
/// The connection string is irrelevant for generating migrations; SACCO_CONNECTION overrides it for `database update`.
/// </summary>
public static class DesignTime
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("SACCO_CONNECTION") ?? "Host=localhost;Port=5432;Database=sacco;Username=sacco;Password=sacco";

    public static DbContextOptions<T> Options<T>(string schema) where T : DbContext =>
        new DbContextOptionsBuilder<T>()
            .UseNpgsql(ConnectionString, n =>
            {
                n.MigrationsAssembly(DbContextRegistration.MigrationsAssembly);
                n.MigrationsHistoryTable(DbContextRegistration.HistoryTable, schema);
            })
            .UseSnakeCaseNamingConvention()
            .Options;
}

public sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args) => new(DesignTime.Options<PlatformDbContext>(PlatformDbContext.SchemaName), new TenantContext());
}

public sealed class LedgerDbContextFactory : IDesignTimeDbContextFactory<LedgerDbContext>
{
    public LedgerDbContext CreateDbContext(string[] args) => new(DesignTime.Options<LedgerDbContext>(LedgerDbContext.SchemaName), new TenantContext());
}

public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args) => new(DesignTime.Options<IdentityDbContext>(IdentityDbContext.SchemaName), new TenantContext());
}

public sealed class MembersDbContextFactory : IDesignTimeDbContextFactory<MembersDbContext>
{
    public MembersDbContext CreateDbContext(string[] args) => new(DesignTime.Options<MembersDbContext>(MembersDbContext.SchemaName), new TenantContext());
}

public sealed class SavingsDbContextFactory : IDesignTimeDbContextFactory<SavingsDbContext>
{
    public SavingsDbContext CreateDbContext(string[] args) => new(DesignTime.Options<SavingsDbContext>(SavingsDbContext.SchemaName), new TenantContext());
}

public sealed class LendingDbContextFactory : IDesignTimeDbContextFactory<LendingDbContext>
{
    public LendingDbContext CreateDbContext(string[] args) => new(DesignTime.Options<LendingDbContext>(LendingDbContext.SchemaName), new TenantContext());
}

public sealed class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args) => new(DesignTime.Options<PaymentsDbContext>(PaymentsDbContext.SchemaName), new TenantContext());
}

public sealed class ReportingDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args) => new(DesignTime.Options<ReportingDbContext>(ReportingDbContext.SchemaName), new TenantContext());
}

public sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args) => new(DesignTime.Options<NotificationsDbContext>(NotificationsDbContext.SchemaName), new TenantContext());
}
