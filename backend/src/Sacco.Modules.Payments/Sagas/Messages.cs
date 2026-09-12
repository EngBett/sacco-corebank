using Wolverine;

namespace Sacco.Modules.Payments.Sagas;

// Every message carries the tenant because sagas run outside an HTTP request.
public sealed record StartCollection(Guid Id, Guid TenantId, string TenantSlug);
public sealed record StartDisbursement(Guid Id, Guid TenantId, string TenantSlug);
public sealed record CollectionCallbackReceived(Guid Id, Guid TenantId, string TenantSlug, Sacco.Shared.Payments.ProviderEvent Event);
public sealed record DisbursementCallbackReceived(Guid Id, Guid TenantId, string TenantSlug, Sacco.Shared.Payments.ProviderEvent Event);
public sealed record CollectionTimeout(Guid Id, Guid TenantId, string TenantSlug) : TimeoutMessage(TimeSpan.FromMinutes(3));
public sealed record DisbursementTimeout(Guid Id, Guid TenantId, string TenantSlug) : TimeoutMessage(TimeSpan.FromMinutes(3));
/// <summary>Sandbox only: deliver the simulated callback after a delay so a demo shows the async flow. One message type per saga (Wolverine binds a message type to a single saga).</summary>
public sealed record SimulateCollectionCallback(Guid Id, Guid TenantId, string TenantSlug);
public sealed record SimulateDisbursementCallback(Guid Id, Guid TenantId, string TenantSlug);
