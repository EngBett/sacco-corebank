using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Platform.Domain;
using Sacco.Modules.Platform.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Platform.Application;

/// <summary>
/// Writes the audit trail (ADR 0019). Each row is enriched with request context and chained to the previous row for
/// this tenant. A transaction-scoped Postgres advisory lock serialises writers per tenant, so two concurrent actions
/// can't both chain onto the same predecessor and leave a fork.
/// </summary>
public sealed class AuditLogger(PlatformDbContext db, ITenantContext tenant, IAuditContext context, IClock clock) : IAuditLogger
{
    public async Task RecordAsync(AuditEvent e, CancellationToken ct)
    {
        var tenantId = tenant.TenantId;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Held until this transaction ends; keyed on the tenant so other tenants are never blocked.
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({tenantId.ToString()}, 0))", ct);

            var previousHash = await db.AuditLog.AsNoTracking()
                .Where(a => a.TenantId == tenantId)
                .OrderByDescending(a => a.Id)
                .Select(a => a.Hash)
                .FirstOrDefaultAsync(ct);

            db.AuditLog.Add(AuditLogEntry.Create(tenantId, clock.UtcNow, e, context, previousHash));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }
}

/// <param name="FirstBrokenEntryId">The earliest entry whose hash doesn't match its contents and predecessor.</param>
public sealed record AuditChainCheck(int Checked, bool Intact, Guid? FirstBrokenEntryId, DateTimeOffset? FirstBrokenAt, string? Problem, int Unchained = 0);

/// <summary>Recomputes the hash chain so an auditor can show the trail hasn't been edited (ADR 0019).</summary>
public sealed class AuditVerifier(PlatformDbContext db)
{
    public async Task<AuditChainCheck> VerifyAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var query = db.AuditLog.AsNoTracking().OrderBy(a => a.Id).AsQueryable();
        if (from is { } f) query = query.Where(a => a.OccurredAt >= f);
        if (to is { } t) query = query.Where(a => a.OccurredAt <= t);

        string? expectedPrevious = null;
        var first = true;
        var checkedCount = 0;
        var unchained = 0;

        await foreach (var entry in query.AsAsyncEnumerable().WithCancellation(ct))
        {
            // Entries written before the chain existed carry no hash; they are reported, not treated as tampered.
            if (string.IsNullOrEmpty(entry.Hash)) { unchained++; continue; }
            checkedCount++;
            // Over a window the first row's predecessor is outside it, so only its own digest is checked.
            if (!first && entry.PreviousHash != expectedPrevious)
                return new AuditChainCheck(checkedCount, false, entry.Id, entry.OccurredAt, "An entry is missing or was inserted out of order.", unchained);
            if (entry.ComputeHash() != entry.Hash)
                return new AuditChainCheck(checkedCount, false, entry.Id, entry.OccurredAt, "An entry's contents no longer match its hash.", unchained);
            expectedPrevious = entry.Hash;
            first = false;
        }

        return new AuditChainCheck(checkedCount, true, null, null, null, unchained);
    }
}
