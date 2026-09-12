namespace Sacco.Modules.Identity.Domain;

/// <summary>Operational store row for IdentityServer (refresh tokens, authorization codes, reference tokens). Not tenant-scoped: keyed by the grant key.</summary>
public class PersistedGrantRecord
{
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? SubjectId { get; set; }
    public string? SessionId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? Expiration { get; set; }
    public DateTime? ConsumedTime { get; set; }
    public string Data { get; set; } = string.Empty;
}
