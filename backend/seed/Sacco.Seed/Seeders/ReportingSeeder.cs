using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sacco.Modules.Reporting.Application;
using Sacco.Modules.Reporting.Persistence;
using Sacco.Seed.Data;
using Sacco.Shared.Time;

namespace Sacco.Seed.Seeders;

/// <summary>A generated statutory return for the last completed quarter, awaiting submission by the compliance officer,
/// plus a demo recipient for the nightly PDF digest (ADR 0013) so the feature is demoable without an admin visit first.</summary>
public sealed class ReportingSeeder(ReportingDbContext db, StatutoryReportService reports, DailyDigestService digest, IClock clock, ILogger<ReportingSeeder> logger) : ISeeder
{
    public int Order => 50;

    public async Task SeedAsync(CancellationToken ct)
    {
        if (!await db.Returns.AnyAsync(ct))
        {
            var today = clock.Today;
            var quarterStartMonth = ((today.Month - 1) / 3) * 3 + 1;
            var periodEnd = new DateOnly(today.Year, quarterStartMonth, 1).AddDays(-1); // last day of the previous quarter
            var periodStart = new DateOnly(periodEnd.Year, ((periodEnd.Month - 1) / 3) * 3 + 1, 1);
            var ret = await reports.GenerateAsync(periodStart, periodEnd, DemoTenant.Users.Accountant, ct);
            logger.Created($"Statutory return {periodStart:yyyy-MM-dd}..{periodEnd:yyyy-MM-dd} (reconciled: {ret.IsReconciled})", 1);
        }

        if (!await db.Recipients.AnyAsync(ct))
        {
            await digest.AddRecipientAsync("admin@icodeio.example.co.ke", "Grace Wanjiku (System Admin)", DemoTenant.Users.Admin, ct);
            logger.Created("Daily digest recipient admin@icodeio.example.co.ke", 1);
        }
    }
}
