using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Identity.Domain;

/// <summary>
/// A member's self-service login: phone number + PIN, bound to a member id. Deliberately separate from
/// <see cref="StaffUser"/>: no roles, no permission bundles — the resolver grants the fixed self-service set
/// and every self endpoint scopes to the member id in the token.
/// </summary>
public class MemberLogin : TenantEntity
{
    private MemberLogin() { }

    public Guid MemberId { get; private set; }
    public string PhoneNumber { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string PinHash { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTimeOffset? LockedOutUntil { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(30);

    public static MemberLogin Create(Guid tenantId, Guid memberId, string displayName, string phoneNumber, string pinHash, Guid createdBy, DateTimeOffset now)
    {
        var phone = NormalisePhone(phoneNumber);
        return new MemberLogin { Id = Ids.New(), TenantId = tenantId, MemberId = memberId, DisplayName = displayName.Trim(), PhoneNumber = phone, PinHash = pinHash, IsActive = true, CreatedAt = now, CreatedByUserId = createdBy };
    }

    public static string NormalisePhone(string phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0") && digits.Length == 10) digits = "254" + digits[1..];
        if (digits.Length != 12 || !digits.StartsWith("254")) throw new DomainRuleException("identity.member_login.phone_invalid", "Phone number must be a Kenyan mobile number (2547XXXXXXXX or 07XXXXXXXX).");
        return digits;
    }

    public static void ValidatePin(string pin)
    {
        if (pin is null || pin.Length is < 4 or > 6 || !pin.All(char.IsDigit)) throw new DomainRuleException("identity.member_login.pin_invalid", "PIN must be 4 to 6 digits.");
        if (pin.Distinct().Count() == 1 || pin is "1234" or "123456" or "0000" or "000000") throw new DomainRuleException("identity.member_login.pin_weak", "Choose a less predictable PIN.");
    }

    public bool IsLockedOut(DateTimeOffset now) => LockedOutUntil is { } until && until > now;
    public void RecordFailedLogin(DateTimeOffset now) { FailedAttempts++; if (FailedAttempts >= MaxFailedAttempts) { LockedOutUntil = now + LockoutDuration; FailedAttempts = 0; } }
    public void RecordSuccessfulLogin(DateTimeOffset now) { FailedAttempts = 0; LockedOutUntil = null; LastLoginAt = now; }
    /// <summary>A correct PIN re-entered inside a session (e.g. to show balances) clears the failure count without counting as a sign-in.</summary>
    public void RecordSuccessfulPinCheck() { FailedAttempts = 0; }
    public void Reset(string displayName, string phoneNumber, string pinHash) { DisplayName = displayName.Trim(); PhoneNumber = NormalisePhone(phoneNumber); PinHash = pinHash; IsActive = true; FailedAttempts = 0; LockedOutUntil = null; }
    public void Deactivate() => IsActive = false;
}
