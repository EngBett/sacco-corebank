namespace Sacco.Shared.Auth;

/// <summary>
/// Read-only lookup of staff users for cross-module fan-out (notifications). Implemented by the
/// Identity module; consumers never see roles, only the permission → users answer.
/// </summary>
public interface IUserDirectory
{
    /// <summary>Active users in the tenant whose roles grant <paramref name="permission"/>.</summary>
    Task<IReadOnlyList<Guid>> UsersWithPermissionAsync(Guid tenantId, string permission, CancellationToken ct);
    /// <summary>Delivery details for out-of-band notifications (SMS/email). Null when the user is unknown or inactive.</summary>
    Task<UserContact?> GetContactAsync(Guid tenantId, Guid userId, CancellationToken ct);
}

public sealed record UserContact(Guid UserId, string DisplayName, string? Email, string? PhoneNumber);
