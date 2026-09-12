using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Lending.Domain;
using Sacco.Modules.Lending.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Lending;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Lending.Application;

public sealed record AgingRow(Guid LoanId, string LoanNumber, Guid MemberId, string ProductCode, Segment Segment, int DaysInArrears, string Bucket, int ProvisionRateBps, decimal OutstandingPrincipal, decimal ArrearsAmount, decimal ProvisionRequired);
public sealed record AgingBucketTotal(string Bucket, int ProvisionRateBps, int Loans, decimal Outstanding, decimal ProvisionRequired);
public sealed record AgingReport(DateOnly AsOf, IReadOnlyList<AgingRow> Loans, IReadOnlyList<AgingBucketTotal> Buckets, decimal TotalOutstanding, decimal TotalProvisionRequired, decimal NonPerformingOutstanding);

/// <summary>NPL aging and provisioning. Rates/buckets come from <see cref="ProvisioningConfig"/>; posting the provision movement is maker-checker.</summary>
public sealed class ProvisioningService(LendingDbContext db, ILedgerService ledger, ITenantContext tenant, IClock clock, IAuditLogger audit)
{
    public async Task<ProvisioningConfig> GetConfigAsync(CancellationToken ct)
        => await db.ProvisioningConfigs.FirstOrDefaultAsync(ct) ?? throw new DomainRuleException("loans.provisioning.not_configured", "Provisioning buckets have not been configured for this SACCO.");

    public async Task<ProvisioningConfig> SetConfigAsync(IReadOnlyList<AgingBucketDraft> buckets, string? source, Guid byUser, CancellationToken ct)
    {
        var existing = await db.ProvisioningConfigs.FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            existing = ProvisioningConfig.Create(Ids.New(), tenant.TenantId, buckets, source, byUser, clock.UtcNow);
            db.ProvisioningConfigs.Add(existing);
        }
        else
        {
            db.AddRange(existing.Replace(buckets, source, byUser, clock.UtcNow));
        }
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("loans.provisioning.config_changed", nameof(ProvisioningConfig), existing.Id.ToString(), byUser,
            "{\"buckets\":[" + string.Join(",", buckets.Select(b => $"{{\"name\":\"{b.Name}\",\"minDays\":{b.MinDaysInArrears},\"rateBps\":{b.ProvisionRateBps}}}")) + "]}"), ct);
        return existing;
    }

    public async Task<AgingReport> AgingAsync(DateOnly asOf, CancellationToken ct)
    {
        var config = await GetConfigAsync(ct);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        // Loans that were on the books at asOf: disbursed on or before it and not yet closed/written off by then.
        var asOfEnd = new DateTimeOffset(asOf.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero);
        var loans = await db.Loans.AsNoTracking()
            .Where(l => l.DisbursementDate != null && l.DisbursementDate <= asOf && (l.Status == LoanStatus.Active || (l.Status == LoanStatus.Closed && l.ClosedAt > asOfEnd)))
            .ToListAsync(ct);
        var rows = new List<AgingRow>();
        var isToday = asOf >= clock.Today;
        foreach (var loan in loans)
        {
            var product = products[loan.ProductId];
            // Running balance for today; statement closing balance for a historical date, so returns reconcile to the trial balance of that date.
            var outstanding = isToday
                ? (await ledger.FindAccountAsync(loan.LedgerAccountNumber!, ct))?.Balance ?? 0
                : (await ledger.GetStatementAsync(loan.LedgerAccountNumber!, new DateOnly(2000, 1, 1), asOf, ct))?.ClosingBalance ?? 0;
            if (outstanding <= 0) continue;
            var days = loan.DaysInArrears(asOf, product.GracePeriodDays);
            var bucket = config.Classify(days);
            var provision = decimal.Round(outstanding * bucket.ProvisionRateBps / 10_000m, 2, MidpointRounding.ToEven);
            rows.Add(new AgingRow(loan.Id, loan.LoanNumber, loan.MemberId, product.Code, product.Segment, days, bucket.Name, bucket.ProvisionRateBps, outstanding, loan.ArrearsAmount(asOf), provision));
        }
        var buckets = config.Buckets.Select(b =>
        {
            var inBucket = rows.Where(r => r.Bucket == b.Name).ToList();
            return new AgingBucketTotal(b.Name, b.ProvisionRateBps, inBucket.Count, inBucket.Sum(r => r.OutstandingPrincipal), inBucket.Sum(r => r.ProvisionRequired));
        }).ToList();
        // SASRA: non-performing = anything past the first (performing) bucket.
        var performing = config.Buckets.First().Name;
        return new AgingReport(asOf, rows, buckets, rows.Sum(r => r.OutstandingPrincipal), rows.Sum(r => r.ProvisionRequired), rows.Where(r => r.Bucket != performing).Sum(r => r.OutstandingPrincipal));
    }

    public async Task<ProvisioningRun> ComputeRunAsync(DateOnly asOf, Guid byUser, CancellationToken ct)
    {
        if (await db.ProvisioningRuns.AnyAsync(r => r.Status == ProvisioningRunStatus.PendingApproval, ct))
            throw new ConflictException("loans.provisioning.run_pending", "A provisioning run is already awaiting approval.");
        var report = await AgingAsync(asOf, ct);
        var run = ProvisioningRun.Create(Ids.New(), tenant.TenantId, asOf, byUser, clock.UtcNow,
            report.Loans.Select(r => new ProvisioningLineDraft(r.LoanId, r.LoanNumber, r.MemberId, r.ProductCode, r.Segment, r.DaysInArrears, r.Bucket, r.ProvisionRateBps, r.OutstandingPrincipal, r.ArrearsAmount, r.ProvisionRequired)));
        db.ProvisioningRuns.Add(run);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("loans.provisioning.computed", nameof(ProvisioningRun), run.Id.ToString(), byUser, $$"""{"asOf":"{{asOf:yyyy-MM-dd}}","required":{{run.TotalProvisionRequired}}}"""), ct);
        return run;
    }

    public async Task<ProvisioningRun> GetRunAsync(Guid id, CancellationToken ct)
        => await db.ProvisioningRuns.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Provisioning run", id);

    /// <summary>Checker approval posts, per product GL pair, the movement between the provision balance already in the GL and the required provision.</summary>
    public async Task<ProvisioningRun> ApproveRunAsync(Guid id, Guid byUser, CancellationToken ct)
    {
        var run = await GetRunAsync(id, ct);
        run.Approve(byUser, clock.UtcNow);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Code, ct);
        foreach (var group in run.Lines.GroupBy(l => (products[l.ProductCode].ProvisionGlAccountCode, products[l.ProductCode].ProvisionExpenseGlAccountCode, l.Segment)))
        {
            var (provisionGl, expenseGl, segment) = group.Key;
            var required = group.Sum(l => l.ProvisionRequired);
            var current = await ledger.GetGlBalanceAsync(provisionGl, ct);
            var delta = required - current;
            if (delta == 0) continue;
            var lines = delta > 0
                ? new List<PostingLine> { new(expenseGl, segment, EntryDirection.Debit, delta, Narrative: "Provision increase"), new(provisionGl, segment, EntryDirection.Credit, delta, Narrative: "Provision increase") }
                : new List<PostingLine> { new(provisionGl, segment, EntryDirection.Debit, -delta, Narrative: "Provision release"), new(expenseGl, segment, EntryDirection.Credit, -delta, Narrative: "Provision release") };
            await ledger.PostAsync(new PostingRequest($"PROV:{run.AsOf:yyyyMMdd}:{provisionGl}", $"Loan loss provisioning as of {run.AsOf:yyyy-MM-dd} ({segment})", run.AsOf, "Lending", byUser, lines), ct);
        }
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("loans.provisioning.posted", nameof(ProvisioningRun), run.Id.ToString(), byUser, $$"""{"computedBy":"{{run.ComputedByUserId}}","required":{{run.TotalProvisionRequired}}}"""), ct);
        return run;
    }

    public async Task<ProvisioningRun> RejectRunAsync(Guid id, string reason, Guid byUser, CancellationToken ct)
    {
        var run = await GetRunAsync(id, ct);
        run.Reject(byUser, reason);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("loans.provisioning.rejected", nameof(ProvisioningRun), run.Id.ToString(), byUser, $$"""{"reason":"{{reason.Replace("\"", "'")}}"}"""), ct);
        return run;
    }
}
