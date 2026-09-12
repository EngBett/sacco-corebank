using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sacco.Modules.Members.Application;
using Sacco.Modules.Members.Domain;
using Sacco.Modules.Members.Persistence;
using Sacco.Seed.Data;
using Sacco.Shared.Members;

namespace Sacco.Seed.Seeders;

/// <summary>Members across every KYC state, registered by the loan officer and verified by the compliance officer (segregated), plus a pending public application.</summary>
public sealed class MembersSeeder(MembersDbContext db, MemberService members, Sacco.Shared.Time.IClock clock, ILogger<MembersSeeder> logger) : ISeeder
{
    public int Order => 15;

    public async Task SeedAsync(CancellationToken ct)
    {
        var existing = await db.Members.Select(m => m.MemberNumber).ToHashSetAsync(ct);
        var created = 0;
        var index = 0;
        foreach (var p in KenyanNames.Members)
        {
            index++;
            if (existing.Contains(p.MemberNumber)) continue;
            // Established members joined 12–13 months ago (matching the ledger history); pending/rejected ones are recent registrations.
            var joinedAt = p.HasAccounts ? clock.Today.AddMonths(-13).AddDays(index * 2) : clock.Today.AddDays(-10 - index);
            var member = await members.RegisterAsync(DemoTenant.MemberId(p.MemberNumber), p.MemberNumber, Details(p), Kin(p), MemberSource.StaffRegistered, null, DemoTenant.Users.LoanOfficer, ct, joinedAt);
            await members.AddDocumentAsync(member.Id, KycDocumentType.NationalIdFront, $"kyc/{p.MemberNumber}/id-front.jpg", DemoTenant.Users.LoanOfficer, ct);
            await members.AddDocumentAsync(member.Id, KycDocumentType.NationalIdBack, $"kyc/{p.MemberNumber}/id-back.jpg", DemoTenant.Users.LoanOfficer, ct);
            await members.AddDocumentAsync(member.Id, KycDocumentType.PassportPhoto, $"kyc/{p.MemberNumber}/photo.jpg", DemoTenant.Users.LoanOfficer, ct);

            switch (p.Kyc)
            {
                case "Verified":
                    await members.VerifyKycAsync(member.Id, DemoTenant.Users.ComplianceOfficer, ct);
                    break;
                case "Suspended":
                    // Verified now; suspended later by SavingsScenarioSeeder once her accounts and history exist.
                    await members.VerifyKycAsync(member.Id, DemoTenant.Users.ComplianceOfficer, ct);
                    break;
                case "Rejected":
                    await members.RejectKycAsync(member.Id, "National ID number could not be verified against IPRS", DemoTenant.Users.ComplianceOfficer, ct);
                    break;
                // PendingVerification: leave as registered
            }
            created++;
        }
        logger.Created("Members", created);

        // Make sure the member-number sequence continues after the seeded numbers.
        var max = KenyanNames.Members.Max(m => int.Parse(m.MemberNumber[1..]));
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO members.member_number_sequences (tenant_id, next_value) VALUES ({DemoTenant.Id}, {max + 1})
            ON CONFLICT (tenant_id) DO UPDATE SET next_value = GREATEST(members.member_number_sequences.next_value, {max + 1})
            """, ct);

        var applicant = KenyanNames.PendingApplicant;
        var hasApplication = await db.Applications.AnyAsync(a => a.Details.NationalIdNumber == applicant.NationalId, ct);
        if (!hasApplication)
        {
            await members.SubmitApplicationAsync(Details(applicant), Kin(applicant), "PublicSite", "203.0.113.7", botCheckPassed: true, ct);
            logger.Created("Pending membership application", 1);
        }
    }

    private static PersonalDetails Details(DemoPerson p) => new()
    {
        FirstName = p.FirstName, LastName = p.LastName, Gender = p.Gender == "F" ? Gender.Female : Gender.Male, DateOfBirth = p.DateOfBirth,
        NationalIdNumber = p.NationalId, KraPin = $"A{p.NationalId}Z", PhoneNumber = p.Phone, Email = p.Email, County = p.County,
        Occupation = p.Occupation, Employer = p.Employer, EmployeeNumber = p.Employer == "Self-employed" || p.Employer == "Retired" ? null : $"EMP{p.NationalId[^4..]}",
        PostalAddress = $"P.O. Box {p.NationalId[^3..]}, {p.County}",
    };

    private static NextOfKin Kin(DemoPerson p) => new() { Name = $"{(p.Gender == "F" ? "John" : "Mary")} {p.LastName}", Relationship = "Spouse", PhoneNumber = "2547001009" + p.NationalId[^2..] };
}
