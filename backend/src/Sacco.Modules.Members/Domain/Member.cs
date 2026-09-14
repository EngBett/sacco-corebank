using Sacco.Shared.Domain;
using Sacco.Shared.Members;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Members.Domain;

public enum Gender { Female = 1, Male = 2, Other = 3 }
public enum MemberSource { StaffRegistered = 1, PublicApplication = 2, Migrated = 3 }
public enum KycDocumentType { NationalIdFront = 1, NationalIdBack = 2, PassportPhoto = 3, Signature = 4, ProofOfIncome = 5, KraPinCertificate = 6 }

/// <summary>Personal details shared by members and applications. Owned value object.</summary>
public class PersonalDetails
{
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public Gender Gender { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public string NationalIdNumber { get; set; } = string.Empty;
    public string? KraPin { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PostalAddress { get; set; }
    public string County { get; set; } = string.Empty;
    public string? Occupation { get; set; }
    public string? Employer { get; set; }
    /// <summary>Payroll/staff number used for check-off deductions.</summary>
    public string? EmployeeNumber { get; set; }

    public string FullName => string.IsNullOrWhiteSpace(MiddleName) ? $"{FirstName} {LastName}" : $"{FirstName} {MiddleName} {LastName}";

    public void Validate(DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName))
            throw new DomainRuleException("members.name_required", "First and last name are required.");
        if (!NationalIdNumber.All(char.IsDigit) || NationalIdNumber.Length is < 6 or > 9)
            throw new DomainRuleException("members.national_id_invalid", "National ID number must be 6–9 digits.");
        if (!(PhoneNumber.StartsWith("254") && PhoneNumber.Length == 12 && PhoneNumber.All(char.IsDigit)))
            throw new DomainRuleException("members.phone_invalid", "Phone number must be in international format, e.g. 254712345678.");
        var age = today.Year - DateOfBirth.Year - (today < DateOfBirth.AddYears(today.Year - DateOfBirth.Year) ? 1 : 0);
        if (age < 18)
            throw new DomainRuleException("members.underage", "Members must be at least 18 years old.");
        if (string.IsNullOrWhiteSpace(County))
            throw new DomainRuleException("members.county_required", "County is required.");
        PhoneNumber = PhoneNumber.Trim();
        NationalIdNumber = NationalIdNumber.Trim();
        Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim().ToLowerInvariant();
    }
}

public class NextOfKin
{
    public string Name { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// A SACCO member. KYC verification is segregated: the user who registered the member (or
/// converted their application) cannot be the one who verifies them.
/// </summary>
public class Member : TenantEntity
{
    private readonly List<KycDocument> _documents = [];
    private Member() { }

    public string MemberNumber { get; private set; } = string.Empty;
    public PersonalDetails Details { get; private set; } = new();
    public NextOfKin NextOfKin { get; private set; } = new();
    public KycStatus KycStatus { get; private set; }
    public MemberSource Source { get; private set; }
    public Guid? ApplicationId { get; private set; }
    public DateOnly JoinedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid RegisteredByUserId { get; private set; }
    public Guid? KycVerifiedByUserId { get; private set; }
    public DateTimeOffset? KycVerifiedAt { get; private set; }
    public string? KycRejectionReason { get; private set; }
    public string? SuspensionReason { get; private set; }
    public DateTimeOffset? SuspendedAt { get; private set; }
    public DateTimeOffset? ExitedAt { get; private set; }
    public Guid? ExitRequestedByUserId { get; private set; }
    public DateTimeOffset? ExitRequestedAt { get; private set; }
    public string? ExitReason { get; private set; }
    public Guid? ExitApprovedByUserId { get; private set; }
    /// <summary>JSON summary of what was settled and paid out when the member left.</summary>
    public string? ExitSettlementJson { get; private set; }
    public IReadOnlyList<KycDocument> Documents => _documents;

    public static Member Register(Guid id, Guid tenantId, string memberNumber, PersonalDetails details, NextOfKin nextOfKin, MemberSource source, Guid? applicationId, Guid registeredBy, DateOnly today, DateTimeOffset now, DateOnly? joinedAt = null)
    {
        details.Validate(today);
        if (joinedAt is DateOnly j && j > today) throw new DomainRuleException("members.joined_in_future", "Join date cannot be in the future.");
        if (registeredBy == Guid.Empty) throw new DomainRuleException("members.registrar_required", "Registering user is required.");
        return new Member
        {
            Id = id, TenantId = tenantId, MemberNumber = memberNumber, Details = details, NextOfKin = nextOfKin,
            KycStatus = KycStatus.PendingVerification, Source = source, ApplicationId = applicationId,
            JoinedAt = joinedAt ?? today, CreatedAt = now, RegisteredByUserId = registeredBy,
        };
    }

    public void UpdateDetails(PersonalDetails details, NextOfKin nextOfKin, DateOnly today)
    {
        if (KycStatus == KycStatus.Exited) throw new DomainRuleException("members.exited", "An exited member cannot be edited.");
        details.Validate(today);
        Details = details; NextOfKin = nextOfKin;
    }

    /// <summary>Returns the new document so the caller can register it as Added (EF treats key-bearing children discovered via navigation as existing).</summary>
    public KycDocument AddDocument(KycDocumentType type, string fileReference, Guid uploadedBy, DateTimeOffset now)
    {
        var doc = KycDocument.Create(Id, type, fileReference, uploadedBy, now);
        _documents.Add(doc);
        return doc;
    }

    /// <summary>Checker step. Requires ID front and passport photo on file and a verifier who is not the registrar.</summary>
    public void VerifyKyc(Guid verifiedBy, DateTimeOffset now)
    {
        if (KycStatus is not (KycStatus.PendingVerification or KycStatus.Rejected))
            throw new DomainRuleException("members.kyc.not_pending", $"Member {MemberNumber} is {KycStatus}; only pending or rejected members can be verified.");
        MakerChecker.EnsureDistinct(RegisteredByUserId, verifiedBy, $"KYC verification of {MemberNumber}");
        if (!_documents.Any(d => d.Type == KycDocumentType.NationalIdFront) || !_documents.Any(d => d.Type == KycDocumentType.PassportPhoto))
            throw new DomainRuleException("members.kyc.documents_missing", "National ID (front) and passport photo must be on file before verification.");
        KycStatus = KycStatus.Verified;
        KycVerifiedByUserId = verifiedBy;
        KycVerifiedAt = now;
        KycRejectionReason = null;
    }

    public void RejectKyc(Guid rejectedBy, string reason)
    {
        if (KycStatus != KycStatus.PendingVerification)
            throw new DomainRuleException("members.kyc.not_pending", $"Member {MemberNumber} is not pending verification.");
        MakerChecker.EnsureDistinct(RegisteredByUserId, rejectedBy, $"KYC rejection of {MemberNumber}");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainRuleException("members.kyc.reason_required", "A rejection reason is required.");
        KycStatus = KycStatus.Rejected;
        KycRejectionReason = reason.Trim();
    }

    public void Suspend(string reason, DateTimeOffset now)
    {
        if (KycStatus != KycStatus.Verified) throw new DomainRuleException("members.not_verified", $"Only verified members can be suspended; {MemberNumber} is {KycStatus}.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainRuleException("members.suspension.reason_required", "A suspension reason is required.");
        KycStatus = KycStatus.Suspended; SuspensionReason = reason.Trim(); SuspendedAt = now;
    }

    public void Reinstate()
    {
        if (KycStatus != KycStatus.Suspended) throw new DomainRuleException("members.not_suspended", $"{MemberNumber} is not suspended.");
        KycStatus = KycStatus.Verified; SuspensionReason = null; SuspendedAt = null;
    }

    /// <summary>Maker: records the exit request. Lending is blocked while an exit is pending; savings continue until approval.</summary>
    public void RequestExit(Guid by, string reason, DateTimeOffset now)
    {
        if (KycStatus is not (KycStatus.Verified or KycStatus.Suspended)) throw new DomainRuleException("members.exit.not_eligible", $"{MemberNumber} is {KycStatus}; only verified or suspended members can exit.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainRuleException("members.exit.reason_required", "An exit reason is required.");
        KycStatus = KycStatus.ExitRequested; ExitRequestedByUserId = by; ExitRequestedAt = now; ExitReason = reason.Trim();
    }

    public void CancelExitRequest(Guid by, string reason)
    {
        if (KycStatus != KycStatus.ExitRequested) throw new DomainRuleException("members.exit.not_requested", $"{MemberNumber} has no pending exit request.");
        MakerChecker.EnsureDistinct(ExitRequestedByUserId ?? Guid.Empty, by, $"exit of {MemberNumber}");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainRuleException("members.exit.reason_required", "A reason is required.");
        KycStatus = SuspensionReason is null ? KycStatus.Verified : KycStatus.Suspended;
        ExitRequestedByUserId = null; ExitRequestedAt = null; ExitReason = null;
    }

    /// <summary>Checker: the member leaves. The caller has already settled loans and paid out balances; the summary is kept for the record.</summary>
    public void Exit(Guid approvedBy, string settlementJson, DateTimeOffset now)
    {
        if (KycStatus is KycStatus.Exited) throw new DomainRuleException("members.exited", $"{MemberNumber} has already exited.");
        if (KycStatus != KycStatus.ExitRequested) throw new DomainRuleException("members.exit.not_requested", $"{MemberNumber} has no pending exit request.");
        MakerChecker.EnsureDistinct(ExitRequestedByUserId ?? Guid.Empty, approvedBy, $"exit of {MemberNumber}");
        KycStatus = KycStatus.Exited; ExitedAt = now; ExitApprovedByUserId = approvedBy; ExitSettlementJson = settlementJson;
    }

    public bool IsInGoodStanding => KycStatus == KycStatus.Verified;
}

public class KycDocument
{
    private KycDocument() { }
    public Guid Id { get; private set; }
    public Guid MemberId { get; private set; }
    public KycDocumentType Type { get; private set; }
    /// <summary>Object-storage key or URL; the platform never stores the bytes in Postgres.</summary>
    public string FileReference { get; private set; } = string.Empty;
    public Guid UploadedByUserId { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }

    internal static KycDocument Create(Guid memberId, KycDocumentType type, string fileReference, Guid by, DateTimeOffset now)
        => new() { Id = Ids.New(), MemberId = memberId, Type = type, FileReference = fileReference, UploadedByUserId = by, UploadedAt = now };
}
