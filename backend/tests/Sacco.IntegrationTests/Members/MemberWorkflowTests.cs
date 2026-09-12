using System.Net;
using System.Net.Http.Json;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Members.Domain;
using Sacco.Modules.Members.Endpoints;
using Sacco.Seed.Data;
using Sacco.Shared.Auth;
using Sacco.Shared.Http;
using Sacco.Shared.Members;
using Shouldly;

namespace Sacco.IntegrationTests.Members;

[Collection(DatabaseCollection.Name)]
public sealed class MemberWorkflowTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    public void Dispose() => _factory.Dispose();

    private static PersonalDetailsDto Person(string id, string phone, string first = "Test") =>
        new(first, null, "Person", Gender.Male, new DateOnly(1990, 5, 5), id, null, phone, null, null, "Nairobi", "Trader", "Self-employed", null);

    private static string RandomId() => "91" + Random.Shared.Next(100000, 999999);
    private static string RandomPhone() => "2547001" + Random.Shared.Next(10000, 99999);

    [Fact]
    public async Task Seeded_members_cover_every_kyc_state_and_the_directory_reflects_standing()
    {
        var client = _factory.ClientAs(DemoTenant.Users.LoanOfficer, Permissions.Members.View);
        var all = await (await client.GetAsync("/api/members?pageSize=200")).ReadAs<PagedResult<MemberListItem>>();
        all.TotalCount.ShouldBeGreaterThanOrEqualTo(20);
        all.Items.Count(m => m.MemberNumber.CompareTo("M00021") < 0).ShouldBe(20);
        foreach (var state in new[] { KycStatus.Verified, KycStatus.PendingVerification, KycStatus.Suspended, KycStatus.Rejected })
            all.Items.ShouldContain(m => m.KycStatus == state, $"expected a seeded member in state {state}");

        using var scope = _factory.TenantScope();
        var directory = (IMemberDirectory)scope.ServiceProvider.GetService(typeof(IMemberDirectory))!;
        (await directory.IsInGoodStandingAsync(DemoTenant.MemberId("M00001"), CancellationToken.None)).ShouldBeTrue();
        (await directory.IsInGoodStandingAsync(DemoTenant.MemberId("M00019"), CancellationToken.None)).ShouldBeFalse(); // suspended
        (await directory.IsInGoodStandingAsync(DemoTenant.MemberId("M00017"), CancellationToken.None)).ShouldBeFalse(); // pending
    }

    [Fact]
    public async Task Registrar_cannot_verify_kyc_but_a_compliance_officer_can()
    {
        var officer = _factory.ClientAs(DemoTenant.Users.LoanOfficer, Permissions.Members.Create, Permissions.Members.Edit, Permissions.Members.KycVerify, Permissions.Members.View);
        var compliance = _factory.ClientAs(DemoTenant.Users.ComplianceOfficer, Permissions.Members.KycVerify);

        var created = await (await officer.PostAsJsonAsync("/api/members", new SaveMemberRequest(Person(RandomId(), RandomPhone()), new NextOfKinDto("Jane", "Sister", "254700100050")))).ReadAs<MemberResponse>();
        created.KycStatus.ShouldBe(KycStatus.PendingVerification);
        created.MemberNumber.ShouldStartWith("M000");
        await officer.PostAsJsonAsync($"/api/members/{created.Id}/documents", new AddDocumentRequest(KycDocumentType.NationalIdFront, "kyc/x/front.jpg"));
        await officer.PostAsJsonAsync($"/api/members/{created.Id}/documents", new AddDocumentRequest(KycDocumentType.PassportPhoto, "kyc/x/photo.jpg"));

        var self = await officer.PostAsync($"/api/members/{created.Id}/kyc/verify", null);
        self.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await self.Content.ReadAsStringAsync()).ShouldContain("maker_checker.same_user");

        var verified = await (await compliance.PostAsync($"/api/members/{created.Id}/kyc/verify", null)).ReadAs<MemberResponse>();
        verified.KycStatus.ShouldBe(KycStatus.Verified);
        verified.KycVerifiedByUserId.ShouldBe(DemoTenant.Users.ComplianceOfficer);
    }

    [Fact]
    public async Task Duplicate_national_id_is_a_conflict()
    {
        var officer = _factory.ClientAs(DemoTenant.Users.LoanOfficer, Permissions.Members.Create);
        var id = RandomId();
        (await officer.PostAsJsonAsync("/api/members", new SaveMemberRequest(Person(id, RandomPhone()), new NextOfKinDto("", "", "")))).StatusCode.ShouldBe(HttpStatusCode.Created);
        var dup = await officer.PostAsJsonAsync("/api/members", new SaveMemberRequest(Person(id, RandomPhone()), new NextOfKinDto("", "", "")));
        dup.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Public_application_creates_a_pending_application_never_a_member_and_needs_review_plus_separate_kyc()
    {
        var publicSite = _factory.CreateClient();
        publicSite.DefaultRequestHeaders.Add("X-Tenant", DemoTenant.Slug);
        var id = RandomId();
        var body = new SubmitApplicationRequest(Person(id, RandomPhone(), "Applicant"), new NextOfKinDto("Kin", "Mother", "254700100060"), "sandbox-ok-token");

        // Without the server-to-server key the public endpoint is closed.
        (await publicSite.PostAsJsonAsync("/api/public/membership-applications", body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        publicSite.DefaultRequestHeaders.Add("X-Public-Api-Key", ApiFactory.PublicApiKey);
        var submitted = await publicSite.PostAsJsonAsync("/api/public/membership-applications", body);
        submitted.StatusCode.ShouldBe(HttpStatusCode.Created);
        var appId = System.Text.Json.JsonDocument.Parse(await submitted.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        (await publicSite.PostAsJsonAsync("/api/public/membership-applications", body)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var bot = body with { TurnstileToken = "fail-bot" };
        var botResponse = await publicSite.PostAsJsonAsync("/api/public/membership-applications", bot with { Details = Person(RandomId(), RandomPhone()) });
        botResponse.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // Not a member yet.
        var viewer = _factory.ClientAs(DemoTenant.Users.LoanOfficer, Permissions.Members.View);
        var members = await (await viewer.GetAsync($"/api/members?search={id}")).ReadAs<PagedResult<MemberListItem>>();
        members.TotalCount.ShouldBe(0);

        // Staff review converts it into a *pending-KYC* member; the reviewer is the registrar and cannot verify.
        var manager = _factory.ClientAs(DemoTenant.Users.BranchManager, Permissions.Members.ApplicationsReview, Permissions.Members.KycVerify, Permissions.Members.Edit);
        var approved = await manager.PostAsJsonAsync($"/api/members/applications/{appId}/approve", new ReviewApplicationRequest("Documents look fine"));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var memberId = System.Text.Json.JsonDocument.Parse(await approved.Content.ReadAsStringAsync()).RootElement.GetProperty("member").GetProperty("id").GetGuid();

        var member = await (await viewer.GetAsync($"/api/members/{memberId}")).ReadAs<MemberResponse>();
        member.KycStatus.ShouldBe(KycStatus.PendingVerification);
        member.Source.ShouldBe(MemberSource.PublicApplication);
        member.RegisteredByUserId.ShouldBe(DemoTenant.Users.BranchManager);

        await manager.PostAsJsonAsync($"/api/members/{memberId}/documents", new AddDocumentRequest(KycDocumentType.NationalIdFront, "kyc/app/front.jpg"));
        await manager.PostAsJsonAsync($"/api/members/{memberId}/documents", new AddDocumentRequest(KycDocumentType.PassportPhoto, "kyc/app/photo.jpg"));
        (await manager.PostAsync($"/api/members/{memberId}/kyc/verify", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Seeded_pending_application_exists_for_the_demo()
    {
        var reviewer = _factory.ClientAs(DemoTenant.Users.BranchManager, Permissions.Members.ApplicationsReview);
        var pending = await (await reviewer.GetAsync("/api/members/applications?status=Pending")).ReadAs<PagedResult<ApplicationResponse>>();
        pending.Items.ShouldContain(a => a.Details.NationalIdNumber == KenyanNames.PendingApplicant.NationalId);
    }
}
