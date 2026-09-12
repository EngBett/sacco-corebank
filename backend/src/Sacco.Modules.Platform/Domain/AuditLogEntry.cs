using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Platform.Domain;

/// <summary>Append-only. There is deliberately no update or delete path for these rows.</summary>
public class AuditLogEntry : TenantEntity
{
    private AuditLogEntry() { }

    public DateTimeOffset OccurredAt { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public Guid ActorUserId { get; private set; }
    public string? Details { get; private set; }
    public string? CorrelationId { get; private set; }

    public static AuditLogEntry Create(Guid tenantId, DateTimeOffset occurredAt, string action, string entityType, string entityId, Guid actorUserId, string? details, string? correlationId)
        => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            OccurredAt = occurredAt,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            ActorUserId = actorUserId,
            Details = details,
            CorrelationId = correlationId,
        };
}
