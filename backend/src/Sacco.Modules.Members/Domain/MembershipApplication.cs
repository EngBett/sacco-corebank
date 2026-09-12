using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Members.Domain;

public enum ApplicationStatus { Pending = 1, Approved = 2, Rejected = 3 }

/// <summary>
/// A public membership application (ADR 0007). It is never a member: staff review converts it
/// into a member in PendingVerification status, and a *different* user then verifies KYC.
/// </summary>
public class MembershipApplication : TenantEntity
{
    private MembershipApplication() { }

    public PersonalDetails Details { get; private set; } = new();
    public NextOfKin NextOfKin { get; private set; } = new();
    public ApplicationStatus Status { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public string Channel { get; private set; } = "PublicSite";
    /// <summary>Hashed client IP for abuse investigation — never the raw address.</summary>
    public string? SourceFingerprint { get; private set; }
    public bool BotCheckPassed { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewNotes { get; private set; }
    public Guid? CreatedMemberId { get; private set; }

    public static MembershipApplication Submit(Guid id, Guid tenantId, PersonalDetails details, NextOfKin nextOfKin, string channel, string? fingerprint, bool botCheckPassed, DateOnly today, DateTimeOffset now)
    {
        details.Validate(today);
        if (!botCheckPassed) throw new DomainRuleException("members.application.bot_check_failed", "Bot protection check did not pass.");
        return new MembershipApplication
        {
            Id = id, TenantId = tenantId, Details = details, NextOfKin = nextOfKin, Status = ApplicationStatus.Pending,
            SubmittedAt = now, Channel = channel, SourceFingerprint = fingerprint, BotCheckPassed = true,
        };
    }

    public void Approve(Guid reviewer, Guid memberId, string? notes, DateTimeOffset now)
    {
        if (Status != ApplicationStatus.Pending) throw new DomainRuleException("members.application.not_pending", "Application is not pending.");
        Status = ApplicationStatus.Approved; ReviewedByUserId = reviewer; ReviewedAt = now; ReviewNotes = notes; CreatedMemberId = memberId;
    }

    public void Reject(Guid reviewer, string reason, DateTimeOffset now)
    {
        if (Status != ApplicationStatus.Pending) throw new DomainRuleException("members.application.not_pending", "Application is not pending.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainRuleException("members.application.reason_required", "A rejection reason is required.");
        Status = ApplicationStatus.Rejected; ReviewedByUserId = reviewer; ReviewedAt = now; ReviewNotes = reason.Trim();
    }
}

/// <summary>Per-tenant counter behind member numbers, advanced with an atomic UPDATE (ADR 0004).</summary>
public class MemberNumberSequence
{
    public Guid TenantId { get; set; }
    public int NextValue { get; set; }
}
