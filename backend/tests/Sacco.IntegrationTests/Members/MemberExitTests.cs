using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Members.Endpoints;
using Sacco.Modules.Members.Persistence;
using Sacco.Modules.Savings.Endpoints;
using Sacco.Seed.Data;
using Sacco.Shared.Auth;
using Sacco.Shared.Lending;
using Sacco.Shared.Members;
using Sacco.Shared.Savings;
using Shouldly;

namespace Sacco.IntegrationTests.Members;

/// <summary>Exit is maker-checker and settles everything: loans from deposits, balances paid out, accounts closed, login disabled.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class MemberExitTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    public void Dispose() => _factory.Dispose();

    /// <summary>A brand-new verified member with funded BOSA and FOSA accounts, so the exit touches nobody another test relies on.</summary>
    private async Task<Guid> SimpleCandidate()
    {
        using var scope = _factory.TenantScope();
        var members = scope.ServiceProvider.GetRequiredService<Sacco.Modules.Members.Application.MemberService>();
        var savings = scope.ServiceProvider.GetRequiredService<Sacco.Modules.Savings.Application.SavingsService>();
        var suffix = Random.Shared.Next(100_000, 999_999).ToString();
        var details = new Sacco.Modules.Members.Domain.PersonalDetails { FirstName = "Exit", LastName = "Candidate", Gender = Sacco.Modules.Members.Domain.Gender.Female, DateOfBirth = new DateOnly(1988, 5, 5), NationalIdNumber = $"77{suffix}", PhoneNumber = $"254799{suffix}", KraPin = $"A77{suffix}Z", County = "Nairobi", Occupation = "Trader", Employer = "Self-employed", PostalAddress = "P.O. Box 1" };
        var kin = new Sacco.Modules.Members.Domain.NextOfKin { Name = "Kin", Relationship = "Sibling", PhoneNumber = $"254798{suffix}" };
        var member = await members.RegisterAsync(null, null, details, kin, Sacco.Modules.Members.Domain.MemberSource.StaffRegistered, null, DemoTenant.Users.LoanOfficer, CancellationToken.None);
        await members.AddDocumentAsync(member.Id, Sacco.Modules.Members.Domain.KycDocumentType.NationalIdFront, "id.jpg", DemoTenant.Users.LoanOfficer, CancellationToken.None);
        await members.AddDocumentAsync(member.Id, Sacco.Modules.Members.Domain.KycDocumentType.PassportPhoto, "photo.jpg", DemoTenant.Users.LoanOfficer, CancellationToken.None);
        await members.VerifyKycAsync(member.Id, DemoTenant.Users.ComplianceOfficer, CancellationToken.None);
        var fosa = await savings.OpenAccountAsync(member.Id, Sacco.Seed.Seeders.SavingsSeeder.FosaCurrent, DemoTenant.Users.Teller, CancellationToken.None);
        var bosa = await savings.OpenAccountAsync(member.Id, Sacco.Seed.Seeders.SavingsSeeder.BosaDeposit, DemoTenant.Users.Teller, CancellationToken.None);
        await savings.DepositAsync(new DepositCommand(fosa.AccountNumber, 3_000m, DepositChannel.Cash, $"EXIT-F-{suffix}", "opening", DemoTenant.Users.Teller), CancellationToken.None);
        await savings.DepositAsync(new DepositCommand(bosa.AccountNumber, 12_000m, DepositChannel.Cash, $"EXIT-B-{suffix}", "opening", DemoTenant.Users.Teller), CancellationToken.None);
        return member.Id;
    }

    [Fact]
    public async Task Exit_pays_out_every_balance_closes_accounts_and_needs_a_different_approver()
    {
        var id = await SimpleCandidate();
        var teller = _factory.ClientAs(DemoTenant.Users.Teller, Permissions.Members.View, Permissions.Members.Exit, Permissions.Members.ExitApprove);
        var manager = _factory.ClientAs(DemoTenant.Users.BranchManager, Permissions.Members.View, Permissions.Members.ExitApprove, Permissions.Savings.View);

        var requested = await (await teller.PostAsJsonAsync($"/api/members/{id}/exit/request", new ReasonRequest("Relocating to Uganda"), HttpExtensions.JsonOptions)).ReadAs<MemberResponse>();
        requested.KycStatus.ShouldBe(KycStatus.ExitRequested);
        requested.ExitReason.ShouldBe("Relocating to Uganda");

        // The requester cannot approve their own request even when they hold the approve permission.
        (await teller.PostAsJsonAsync($"/api/members/{id}/exit/approve", new ApproveExitRequest(ExitPayoutChannel.Cash), HttpExtensions.JsonOptions)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var before = await (await manager.GetAsync($"/api/savings/accounts/by-member/{id}")).ReadAs<List<SavingsAccountResponse>>();
        before.ShouldNotBeEmpty();
        var totalBefore = before.Sum(a => a.Balance);

        var exited = await (await manager.PostAsJsonAsync($"/api/members/{id}/exit/approve", new ApproveExitRequest(ExitPayoutChannel.Cash), HttpExtensions.JsonOptions)).ReadAs<MemberResponse>();
        exited.KycStatus.ShouldBe(KycStatus.Exited);
        exited.ExitApprovedByUserId.ShouldBe(DemoTenant.Users.BranchManager);
        exited.ExitSettlementJson.ShouldContain("totalPaid");
        exited.SelfServiceEnabled.ShouldBeFalse();

        var after = await (await manager.GetAsync($"/api/savings/accounts/by-member/{id}")).ReadAs<List<SavingsAccountResponse>>();
        after.ShouldAllBe(a => a.Status == Sacco.Modules.Savings.Domain.SavingsAccountStatus.Closed);
        after.Sum(a => a.Balance).ShouldBe(0m);
        totalBefore.ShouldBeGreaterThan(0m);
        (await teller.PostAsJsonAsync($"/api/members/{id}/exit/request", new ReasonRequest("again"), HttpExtensions.JsonOptions)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_member_who_still_guarantees_others_cannot_exit()
    {
        // M00009's development loan is guaranteed by M00011 and M00012 (seeded); M00011 therefore cannot leave.
        var guarantor = DemoTenant.MemberId("M00011");
        var teller = _factory.ClientAs(DemoTenant.Users.Teller, Permissions.Members.View, Permissions.Members.Exit);
        var manager = _factory.ClientAs(DemoTenant.Users.BranchManager, Permissions.Members.View, Permissions.Members.ExitApprove);
        await teller.PostAsJsonAsync($"/api/members/{guarantor}/exit/request", new ReasonRequest("Leaving"), HttpExtensions.JsonOptions);
        var res = await manager.PostAsJsonAsync($"/api/members/{guarantor}/exit/approve", new ApproveExitRequest(ExitPayoutChannel.Cash), HttpExtensions.JsonOptions);
        res.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await res.Content.ReadAsStringAsync()).ShouldContain("loans.exit.active_guarantees");
        // Still pending: the manager declines it so the member is back in good standing.
        var cancelled = await (await manager.PostAsJsonAsync($"/api/members/{guarantor}/exit/cancel", new ReasonRequest("Guarantees outstanding"), HttpExtensions.JsonOptions)).ReadAs<MemberResponse>();
        cancelled.KycStatus.ShouldBe(KycStatus.Verified);
    }
}
