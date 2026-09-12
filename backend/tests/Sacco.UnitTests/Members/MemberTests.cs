using Sacco.Modules.Members.Domain;
using Sacco.Shared.Domain;
using Sacco.Shared.Members;
using Shouldly;

namespace Sacco.UnitTests.Members;

public class MemberTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Registrar = Guid.NewGuid();
    private static readonly Guid Verifier = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 9, 11);
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);

    private static PersonalDetails Details(string id = "90123401", DateOnly? dob = null) => new()
    {
        FirstName = "Wanjiru", LastName = "Kamau", Gender = Gender.Female, DateOfBirth = dob ?? new DateOnly(1985, 3, 14),
        NationalIdNumber = id, PhoneNumber = "254700100001", County = "Kiambu",
    };

    private static Member NewMember() => Member.Register(Guid.NewGuid(), Tenant, "M00001", Details(), new NextOfKin(), MemberSource.StaffRegistered, null, Registrar, Today, Now);

    private static Member WithDocuments(Member m)
    {
        m.AddDocument(KycDocumentType.NationalIdFront, "a", Registrar, Now);
        m.AddDocument(KycDocumentType.PassportPhoto, "b", Registrar, Now);
        return m;
    }

    [Fact]
    public void New_member_starts_pending_verification()
    {
        NewMember().KycStatus.ShouldBe(KycStatus.PendingVerification);
    }

    [Fact]
    public void Underage_and_malformed_details_are_rejected()
    {
        Should.Throw<DomainRuleException>(() => Member.Register(Guid.NewGuid(), Tenant, "M1", Details(dob: new DateOnly(2010, 1, 1)), new(), MemberSource.StaffRegistered, null, Registrar, Today, Now)).Code.ShouldBe("members.underage");
        Should.Throw<DomainRuleException>(() => Member.Register(Guid.NewGuid(), Tenant, "M1", Details(id: "AB"), new(), MemberSource.StaffRegistered, null, Registrar, Today, Now)).Code.ShouldBe("members.national_id_invalid");
        var badPhone = Details(); badPhone.PhoneNumber = "0712345678";
        Should.Throw<DomainRuleException>(() => Member.Register(Guid.NewGuid(), Tenant, "M1", badPhone, new(), MemberSource.StaffRegistered, null, Registrar, Today, Now)).Code.ShouldBe("members.phone_invalid");
    }

    [Fact]
    public void Registrar_cannot_verify_their_own_registration()
    {
        var m = WithDocuments(NewMember());
        Should.Throw<MakerCheckerViolationException>(() => m.VerifyKyc(Registrar, Now));
        m.KycStatus.ShouldBe(KycStatus.PendingVerification);
    }

    [Fact]
    public void Verification_requires_documents_and_a_different_user()
    {
        var m = NewMember();
        Should.Throw<DomainRuleException>(() => m.VerifyKyc(Verifier, Now)).Code.ShouldBe("members.kyc.documents_missing");
        WithDocuments(m).VerifyKyc(Verifier, Now);
        m.KycStatus.ShouldBe(KycStatus.Verified);
        m.KycVerifiedByUserId.ShouldBe(Verifier);
        m.IsInGoodStanding.ShouldBeTrue();
    }

    [Fact]
    public void Suspension_and_reinstatement_only_apply_to_verified_members()
    {
        var m = NewMember();
        Should.Throw<DomainRuleException>(() => m.Suspend("x", Now)).Code.ShouldBe("members.not_verified");
        WithDocuments(m).VerifyKyc(Verifier, Now);
        m.Suspend("Returned cheques", Now);
        m.KycStatus.ShouldBe(KycStatus.Suspended);
        m.IsInGoodStanding.ShouldBeFalse();
        m.Reinstate();
        m.KycStatus.ShouldBe(KycStatus.Verified);
    }

    [Fact]
    public void Rejected_member_can_be_reverified_after_correction()
    {
        var m = WithDocuments(NewMember());
        m.RejectKyc(Verifier, "ID mismatch");
        m.KycStatus.ShouldBe(KycStatus.Rejected);
        m.VerifyKyc(Verifier, Now);
        m.KycStatus.ShouldBe(KycStatus.Verified);
        m.KycRejectionReason.ShouldBeNull();
    }

    [Fact]
    public void Application_requires_bot_check_and_is_never_a_member()
    {
        Should.Throw<DomainRuleException>(() => MembershipApplication.Submit(Guid.NewGuid(), Tenant, Details(), new(), "PublicSite", null, botCheckPassed: false, Today, Now))
            .Code.ShouldBe("members.application.bot_check_failed");
        var app = MembershipApplication.Submit(Guid.NewGuid(), Tenant, Details(), new(), "PublicSite", "fp", true, Today, Now);
        app.Status.ShouldBe(ApplicationStatus.Pending);
        app.Approve(Verifier, Guid.NewGuid(), null, Now);
        app.Status.ShouldBe(ApplicationStatus.Approved);
        Should.Throw<DomainRuleException>(() => app.Reject(Verifier, "late", Now));
    }
}
