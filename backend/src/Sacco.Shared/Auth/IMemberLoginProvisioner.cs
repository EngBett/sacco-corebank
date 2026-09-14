namespace Sacco.Shared.Auth;

/// <summary>
/// Member self-service logins live in the Identity module but are created from the Members module
/// (staff enable them on a verified member). Login is phone number + PIN; the token carries the member id.
/// </summary>
public interface IMemberLoginProvisioner
{
    Task ProvisionAsync(Guid memberId, string displayName, string phoneNumber, string pin, Guid byUserId, CancellationToken ct);
    Task DeactivateAsync(Guid memberId, Guid byUserId, CancellationToken ct);
    Task<bool> IsEnabledAsync(Guid memberId, CancellationToken ct);
}
