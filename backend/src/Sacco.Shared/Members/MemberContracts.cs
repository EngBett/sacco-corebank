namespace Sacco.Shared.Members;

public enum KycStatus
{
    PendingVerification = 1,
    Verified = 2,
    Rejected = 3,
    Suspended = 4,
    Exited = 5,
}

public sealed record MemberSummary(Guid Id, string MemberNumber, string FullName, string NationalIdNumber, string PhoneNumber, KycStatus KycStatus, DateOnly JoinedAt);

/// <summary>Members module's public surface for Savings/Lending/Payments. Never join to member tables from another module.</summary>
public interface IMemberDirectory
{
    Task<MemberSummary?> FindAsync(Guid memberId, CancellationToken ct);
    Task<MemberSummary?> FindByNumberAsync(string memberNumber, CancellationToken ct);
    Task<MemberSummary?> FindByPhoneAsync(string phoneNumber, CancellationToken ct);
    /// <summary>True only for KYC-verified members in good standing — the gate for any account opening or lending.</summary>
    Task<bool> IsInGoodStandingAsync(Guid memberId, CancellationToken ct);
}
