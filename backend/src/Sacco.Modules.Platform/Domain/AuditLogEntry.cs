using System.Security.Cryptography;
using System.Text;
using Sacco.Shared.Audit;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Platform.Domain;

/// <summary>
/// One entry in the audit trail (ADR 0019). Append-only: the database refuses UPDATE and DELETE on this table, and each
/// row carries the hash of the row before it, so removing or editing history breaks the chain and
/// <c>GET /api/admin/audit-log/verify</c> reports where.
/// </summary>
public class AuditLogEntry : TenantEntity
{
    private AuditLogEntry() { }

    public DateTimeOffset OccurredAt { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public Guid ActorUserId { get; private set; }

    /// <summary>The actor's display name as it was at the time; the user row may be renamed or deleted later.</summary>
    public string? ActorName { get; private set; }
    public AuditOutcome Outcome { get; private set; }
    public string? Details { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }

    /// <summary>Branch of the acting user, so a branch can be audited on its own.</summary>
    public Guid? BranchId { get; private set; }

    /// <summary>Hash of the previous entry for this tenant; null on the first one.</summary>
    public string? PreviousHash { get; private set; }
    public string Hash { get; private set; } = string.Empty;

    public static AuditLogEntry Create(Guid tenantId, DateTimeOffset occurredAt, AuditEvent e, IAuditContext context, string? previousHash)
    {
        var entry = new AuditLogEntry
        {
            // v7 ids sort by creation time, so the chain order matches the id order.
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            OccurredAt = occurredAt,
            Action = Trim(e.Action, 100) ?? "unknown",
            EntityType = Trim(e.EntityType, 100) ?? "unknown",
            EntityId = Trim(e.EntityId, 100) ?? "",
            ActorUserId = e.ActorUserId,
            ActorName = Trim(context.ActorName, 150),
            Outcome = e.Outcome,
            Details = e.Details,
            CorrelationId = Trim(e.CorrelationId ?? context.CorrelationId, 100),
            IpAddress = Trim(context.IpAddress, 45),
            UserAgent = Trim(context.UserAgent, 300),
            BranchId = context.BranchId,
            PreviousHash = previousHash,
        };
        entry.Hash = entry.ComputeHash();
        return entry;
    }

    /// <summary>
    /// SHA-256 over the fields that matter, prefixed with the previous row's hash. Recomputed during verification: any
    /// edited value, or a row removed from the middle, changes the digest and breaks every later link.
    /// </summary>
    public string ComputeHash()
    {
        var payload = string.Join('',
            PreviousHash ?? "",
            Id.ToString(),
            TenantId.ToString(),
            OccurredAt.ToUniversalTime().ToString("O"),
            Action,
            EntityType,
            EntityId,
            ActorUserId.ToString(),
            ActorName ?? "",
            Outcome.ToString(),
            Details ?? "",
            CorrelationId ?? "",
            IpAddress ?? "",
            UserAgent ?? "",
            BranchId?.ToString() ?? "");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static string? Trim(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim() is { } v && v.Length > maxLength ? v[..maxLength] : value.Trim();
}
