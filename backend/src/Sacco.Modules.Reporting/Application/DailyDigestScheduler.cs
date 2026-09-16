using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Reporting.Application;

public sealed record DailyDigestRunResult(int Tenants, int Sent, int Skipped, IReadOnlyList<string> Errors);

/// <summary>
/// Runs the nightly PDF digest for every active tenant at <c>Reporting:DailyDigest:RunAtUtc</c> (ADR 0013),
/// the same "one tenant per DI scope, one tenant's failure never stops the others" shape as
/// <c>LendingMaintenanceService</c>. API host only — the seed tool never runs schedulers.
/// </summary>
public sealed class DailyDigestScheduler(IServiceScopeFactory scopes, IOptions<ReportingSettings> options, IClock clock, ILogger<DailyDigestScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.DailyDigest.Enabled) { logger.LogInformation("Daily digest scheduler disabled"); return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = NextRunDelay(clock.UtcNow, options.Value.DailyDigest.RunAtUtc);
            logger.LogInformation("Daily digest next run in {Delay}", delay);
            try { await Task.Delay(delay, stoppingToken); } catch (OperationCanceledException) { return; }
            var result = await RunOnceAsync(stoppingToken);
            logger.LogInformation("Daily digest: {Tenants} tenant(s), {Sent} sent, {Skipped} skipped (no recipients), {Errors} error(s)", result.Tenants, result.Sent, result.Skipped, result.Errors.Count);
        }
    }

    /// <summary>Time until the next occurrence of <paramref name="runAtUtc"/>; never zero, so a run that finishes at the scheduled minute waits a full day.</summary>
    public static TimeSpan NextRunDelay(DateTimeOffset now, TimeOnly runAtUtc)
    {
        var today = new DateTimeOffset(DateOnly.FromDateTime(now.UtcDateTime).ToDateTime(runAtUtc), TimeSpan.Zero);
        var next = today > now ? today : today.AddDays(1);
        return next - now;
    }

    public async Task<DailyDigestRunResult> RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<(Guid Id, string Slug)> tenantList;
        using (var scope = scopes.CreateScope())
            tenantList = await scope.ServiceProvider.GetRequiredService<ITenantEnumerator>().ListActiveAsync(ct);

        int sent = 0, skipped = 0;
        var errors = new List<string>();
        foreach (var (id, slug) in tenantList)
        {
            try
            {
                using var scope = scopes.CreateScope();
                scope.ServiceProvider.GetRequiredService<TenantContext>().Set(id, slug);
                var result = await scope.ServiceProvider.GetRequiredService<DailyDigestService>().RunForCurrentTenantAsync(ct);
                if (result.Error is not null) errors.Add($"{slug}: {result.Error}");
                else if (result.Sent) sent++;
                else skipped++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Daily digest failed for tenant {Tenant}", slug);
                errors.Add($"{slug}: {ex.Message}");
            }
        }
        return new DailyDigestRunResult(tenantList.Count, sent, skipped, errors);
    }
}
