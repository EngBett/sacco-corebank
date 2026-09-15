using Sacco.Modules.Members.Domain;
using Sacco.Shared.Domain;
using Sacco.Shared.Members;
using Shouldly;

namespace Sacco.UnitTests.Members;

public sealed class MemberExitTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 14);
    private static readonly Guid Registrar = Guid.NewGuid();
    private static readonly Guid Teller = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();

    private static Member Verified()
    {
        var m = Member.Register(Guid.NewGuid(), Guid.NewGuid(), "M00099", new PersonalDetails { FirstName = "Test", LastName = "Member", Gender = Gender.Female, DateOfBirth = new DateOnly(1990, 1, 1), NationalIdNumber = "12345678", PhoneNumber = "254700000099", KraPin = "A12345678Z", County = "Nairobi", Occupation = "x", Employer = "y", PostalAddress = "z" }, new NextOfKin { Name = "K", Relationship = "Spouse", PhoneNumber = "254700000098" }, MemberSource.StaffRegistered, null, Registrar, Today, Now);
        m.AddDocument(KycDocumentType.NationalIdFront, "a", Registrar, Now);
        m.AddDocument(KycDocumentType.PassportPhoto, "b", Registrar, Now);
        m.VerifyKyc(Guid.NewGuid(), Now);
        return m;
    }

    [Fact]
    public void Exit_is_requested_by_one_user_and_approved_by_another()
    {
        var m = Verified();
        m.RequestExit(Teller, "Relocating abroad", Now);
        m.KycStatus.ShouldBe(KycStatus.ExitRequested);
        m.IsInGoodStanding.ShouldBeFalse("no lending while an exit is pending");
        Should.Throw<MakerCheckerViolationException>(() => m.Exit(Teller, "{}", Now));
        m.Exit(Manager, "{\"totalPaid\":1}", Now);
        m.KycStatus.ShouldBe(KycStatus.Exited);
        m.ExitApprovedByUserId.ShouldBe(Manager);
        m.ExitSettlementJson.ShouldContain("totalPaid");
        Should.Throw<DomainRuleException>(() => m.UpdateDetails(m.Details, m.NextOfKin, Today)).Code.ShouldBe("members.exited");
    }

    [Fact]
    public void Exit_cannot_be_approved_without_a_request_and_can_be_declined()
    {
        var m = Verified();
        Should.Throw<DomainRuleException>(() => m.Exit(Manager, "{}", Now)).Code.ShouldBe("members.exit.not_requested");
        m.RequestExit(Teller, "Leaving", Now);
        Should.Throw<MakerCheckerViolationException>(() => m.CancelExitRequest(Teller, "changed mind"));
        m.CancelExitRequest(Manager, "Member withdrew the request");
        m.KycStatus.ShouldBe(KycStatus.Verified);
        m.ExitReason.ShouldBeNull();
    }

    [Fact]
    public void Only_verified_or_suspended_members_can_request_exit()
    {
        var m = Member.Register(Guid.NewGuid(), Guid.NewGuid(), "M00098", new PersonalDetails { FirstName = "P", LastName = "Q", Gender = Gender.Male, DateOfBirth = new DateOnly(1990, 1, 1), NationalIdNumber = "87654321", PhoneNumber = "254700000097", KraPin = "A87654321Z", County = "Nairobi", Occupation = "x", Employer = "y", PostalAddress = "z" }, new NextOfKin { Name = "K", Relationship = "Spouse", PhoneNumber = "254700000096" }, MemberSource.StaffRegistered, null, Registrar, Today, Now);
        Should.Throw<DomainRuleException>(() => m.RequestExit(Teller, "x", Now)).Code.ShouldBe("members.exit.not_eligible");
    }
}
