using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sacco.Modules.Lending.Persistence;
using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Lending.Application;

public sealed record MaintenanceRunResult(int Tenants, int InterestAccruals, int BureauDetailsPurged, IReadOnlyList<string> Errors);

/// <summary>
/// Daily housekeeping that used to need a person: interest accrual on due instalments and purging bureau
/// report details past their retention period. Runs once a day at <c>Lending:Maintenance:RunAtUtc</c> for
/// every active tenant; each tenant is its own scope and failure in one never stops the others.
/// </summary>
public sealed class LendingMaintenanceService(IServiceScopeFactory scopes, IOptions<LendingSettings> options, IClock clock, ILogger<LendingMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Maintenance.Enabled) { logger.LogInformation("Lending maintenance scheduler disabled"); return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = NextRunDelay(clock.UtcNow, options.Value.Maintenance.RunAtUtc);
            logger.LogInformation("Lending maintenance next run in {Delay}", delay);
            try { await Task.Delay(delay, stoppingToken); } catch (OperationCanceledException) { return; }
            var result = await RunOnceAsync(stoppingToken);
            logger.LogInformation("Lending maintenance: {Tenants} tenant(s), {Accruals} accrual(s), {Purged} bureau detail(s) purged, {Errors} error(s)", result.Tenants, result.InterestAccruals, result.BureauDetailsPurged, result.Errors.Count);
        }
    }

    /// <summary>Time until the next occurrence of <paramref name="runAtUtc"/>; never zero, so a run that finishes at the scheduled minute waits a full day.</summary>
    public static TimeSpan NextRunDelay(DateTimeOffset now, TimeOnly runAtUtc)
    {
        var today = new DateTimeOffset(DateOnly.FromDateTime(now.UtcDateTime).ToDateTime(runAtUtc), TimeSpan.Zero);
        var next = today > now ? today : today.AddDays(1);
        return next - now;
    }

    public async Task<MaintenanceRunResult> RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<(Guid Id, string Slug)> tenants;
        using (var scope = scopes.CreateScope())
            tenants = await scope.ServiceProvider.GetRequiredService<ITenantEnumerator>().ListActiveAsync(ct);

        int accruals = 0, purged = 0;
        var errors = new List<string>();
        foreach (var (id, slug) in tenants)
        {
            try
            {
                using var scope = scopes.CreateScope();
                scope.ServiceProvider.GetRequiredService<TenantContext>().Set(id, slug);
                var repayments = scope.ServiceProvider.GetRequiredService<RepaymentService>();
                accruals += await repayments.AccrueInterestAsync(clock.Today, SystemActors.System, ct);
                purged += await PurgeBureauDetailsAsync(scope.ServiceProvider.GetRequiredService<LendingDbContext>(), options.Value.CreditBureau.RetentionDays, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Lending maintenance failed for tenant {Tenant}", slug);
                errors.Add($"{slug}: {ex.Message}");
            }
        }
        return new MaintenanceRunResult(tenants.Count, accruals, purged, errors);
    }

    /// <summary>Bureau narratives and references are personal data held under the bureau agreement; the status and score stay for the credit record.</summary>
    public async Task<int> PurgeBureauDetailsAsync(LendingDbContext db, int retentionDays, CancellationToken ct)
    {
        if (retentionDays <= 0) return 0;
        var cutoff = clock.UtcNow.AddDays(-retentionDays);
        return await db.CreditScores.Where(s => s.ComputedAt < cutoff && (s.BureauNarrative != null || s.BureauReference != null))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.BureauNarrative, (string?)null).SetProperty(x => x.BureauReference, (string?)null), ct);
    }
}
