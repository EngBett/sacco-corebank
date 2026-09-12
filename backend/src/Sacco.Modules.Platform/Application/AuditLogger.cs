using Sacco.Modules.Platform.Domain;
using Sacco.Modules.Platform.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Platform.Application;

public sealed class AuditLogger(PlatformDbContext db, ITenantContext tenant, IClock clock) : IAuditLogger
{
    public async Task RecordAsync(AuditEvent e, CancellationToken ct)
    {
        var entry = AuditLogEntry.Create(tenant.TenantId, clock.UtcNow, e.Action, e.EntityType, e.EntityId, e.ActorUserId, e.Details, e.CorrelationId);
        db.AuditLog.Add(entry);
        await db.SaveChangesAsync(ct);
    }
}
