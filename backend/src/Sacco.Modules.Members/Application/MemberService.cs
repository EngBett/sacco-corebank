using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sacco.Modules.Members.Domain;
using Sacco.Modules.Members.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Domain;
using Sacco.Shared.Members;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Members.Application;

public sealed class MemberService(MembersDbContext db, ITenantContext tenant, IClock clock, IAuditLogger audit)
{
    public async Task<string> NextMemberNumberAsync(CancellationToken ct)
    {
        // Atomic increment (row lock taken by the UPSERT itself); first call for a tenant inserts the row.
        // Raw command because EF's SqlQuery wraps SQL in a subquery, which Postgres does not allow for INSERT ... RETURNING.
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText = """
                INSERT INTO members.member_number_sequences (tenant_id, next_value) VALUES (@tenant, 2)
                ON CONFLICT (tenant_id) DO UPDATE SET next_value = members.member_number_sequences.next_value + 1
                RETURNING next_value - 1
                """;
            var p = cmd.CreateParameter(); p.ParameterName = "tenant"; p.Value = tenant.TenantId; cmd.Parameters.Add(p);
            var next = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            return $"M{next:D5}";
        }
        finally
        {
            if (wasClosed) await db.Database.CloseConnectionAsync();
        }
    }

    /// <param name="joinedAt">Historical join date for migrated/seeded members; defaults to today.</param>
    public async Task<Member> RegisterAsync(Guid? id, string? memberNumber, PersonalDetails details, NextOfKin kin, MemberSource source, Guid? applicationId, Guid registeredBy, CancellationToken ct, DateOnly? joinedAt = null)
    {
        await EnsureNoDuplicateAsync(details.NationalIdNumber, details.PhoneNumber, ct);
        memberNumber ??= await NextMemberNumberAsync(ct);
        var member = Member.Register(id ?? Ids.New(), tenant.TenantId, memberNumber, details, kin, source, applicationId, registeredBy, clock.Today, clock.UtcNow, joinedAt);
        db.Members.Add(member);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("members.registered", nameof(Member), member.Id.ToString(), registeredBy, $$"""{"memberNumber":"{{member.MemberNumber}}","source":"{{source}}"}"""), ct);
        return member;
    }

    public async Task<Member> GetAsync(Guid id, CancellationToken ct)
        => await db.Members.FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw new NotFoundException("Member", id);

    public async Task<Member> UpdateAsync(Guid id, PersonalDetails details, NextOfKin kin, Guid byUser, CancellationToken ct)
    {
        var member = await GetAsync(id, ct);
        if (member.Details.NationalIdNumber != details.NationalIdNumber || member.Details.PhoneNumber != details.PhoneNumber)
            await EnsureNoDuplicateAsync(details.NationalIdNumber, details.PhoneNumber, ct, excludeMemberId: id);
        member.UpdateDetails(details, kin, clock.Today);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("members.updated", nameof(Member), id.ToString(), byUser), ct);
        return member;
    }

    public async Task<Member> AddDocumentAsync(Guid id, KycDocumentType type, string fileReference, Guid byUser, CancellationToken ct)
    {
        var member = await GetAsync(id, ct);
        var doc = member.AddDocument(type, fileReference, byUser, clock.UtcNow);
        db.Add(doc);
        await db.SaveChangesAsync(ct);
        return member;
    }

    public async Task<Member> VerifyKycAsync(Guid id, Guid byUser, CancellationToken ct)
    {
        var member = await GetAsync(id, ct);
        member.VerifyKyc(byUser, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("members.kyc.verified", nameof(Member), id.ToString(), byUser, $$"""{"memberNumber":"{{member.MemberNumber}}","registeredBy":"{{member.RegisteredByUserId}}"}"""), ct);
        return member;
    }

    public async Task<Member> RejectKycAsync(Guid id, string reason, Guid byUser, CancellationToken ct)
    {
        var member = await GetAsync(id, ct);
        member.RejectKyc(byUser, reason);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("members.kyc.rejected", nameof(Member), id.ToString(), byUser, $$"""{"reason":"{{reason.Replace("\"", "'")}}"}"""), ct);
        return member;
    }

    public async Task<Member> SuspendAsync(Guid id, string reason, Guid byUser, CancellationToken ct)
    {
        var member = await GetAsync(id, ct);
        member.Suspend(reason, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("members.suspended", nameof(Member), id.ToString(), byUser, $$"""{"reason":"{{reason.Replace("\"", "'")}}"}"""), ct);
        return member;
    }

    public async Task<Member> ReinstateAsync(Guid id, Guid byUser, CancellationToken ct)
    {
        var member = await GetAsync(id, ct);
        member.Reinstate();
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("members.reinstated", nameof(Member), id.ToString(), byUser), ct);
        return member;
    }

    // ---- Public applications ----

    public async Task<MembershipApplication> SubmitApplicationAsync(PersonalDetails details, NextOfKin kin, string channel, string? remoteIp, bool botCheckPassed, CancellationToken ct)
    {
        var existingMember = await db.Members.AnyAsync(m => m.Details.NationalIdNumber == details.NationalIdNumber && m.KycStatus != KycStatus.Exited, ct);
        if (existingMember)
            throw new ConflictException("members.application.already_member", "A member with this national ID already exists. Please contact the SACCO.");
        var pending = await db.Applications.AnyAsync(a => a.Details.NationalIdNumber == details.NationalIdNumber && a.Status == ApplicationStatus.Pending, ct);
        if (pending)
            throw new ConflictException("members.application.duplicate", "An application with this national ID is already awaiting review.");

        var fingerprint = remoteIp is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(remoteIp + tenant.TenantId)))[..32];
        var app = MembershipApplication.Submit(Ids.New(), tenant.TenantId, details, kin, channel, fingerprint, botCheckPassed, clock.Today, clock.UtcNow);
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        return app;
    }

    public async Task<MembershipApplication> GetApplicationAsync(Guid id, CancellationToken ct)
        => await db.Applications.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Membership application", id);

    /// <summary>Converts a reviewed application into a member in PendingVerification. The reviewer becomes the registrar and therefore cannot verify KYC.</summary>
    public async Task<(MembershipApplication Application, Member Member)> ApproveApplicationAsync(Guid id, string? notes, Guid reviewer, CancellationToken ct)
    {
        var app = await GetApplicationAsync(id, ct);
        if (app.Status != ApplicationStatus.Pending) throw new DomainRuleException("members.application.not_pending", "Application is not pending.");
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var member = await RegisterAsync(null, null, Clone(app.Details), Clone(app.NextOfKin), MemberSource.PublicApplication, app.Id, reviewer, ct);
            app.Approve(reviewer, member.Id, notes, clock.UtcNow);
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new AuditEvent("members.application.approved", nameof(MembershipApplication), app.Id.ToString(), reviewer, $$"""{"memberId":"{{member.Id}}"}"""), ct);
            await tx.CommitAsync(ct);
            return (app, member);
        });
    }

    public async Task<MembershipApplication> RejectApplicationAsync(Guid id, string reason, Guid reviewer, CancellationToken ct)
    {
        var app = await GetApplicationAsync(id, ct);
        app.Reject(reviewer, reason, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("members.application.rejected", nameof(MembershipApplication), app.Id.ToString(), reviewer, $$"""{"reason":"{{reason.Replace("\"", "'")}}"}"""), ct);
        return app;
    }

    private async Task EnsureNoDuplicateAsync(string nationalId, string phone, CancellationToken ct, Guid? excludeMemberId = null)
    {
        var q = db.Members.Where(m => m.KycStatus != KycStatus.Exited);
        if (excludeMemberId is Guid ex) q = q.Where(m => m.Id != ex);
        if (await q.AnyAsync(m => m.Details.NationalIdNumber == nationalId, ct))
            throw new ConflictException("members.duplicate_national_id", $"A member with national ID {nationalId} already exists.");
        if (await q.AnyAsync(m => m.Details.PhoneNumber == phone, ct))
            throw new ConflictException("members.duplicate_phone", $"A member with phone number {phone} already exists.");
    }

    private static PersonalDetails Clone(PersonalDetails d) => new()
    {
        FirstName = d.FirstName, MiddleName = d.MiddleName, LastName = d.LastName, Gender = d.Gender, DateOfBirth = d.DateOfBirth,
        NationalIdNumber = d.NationalIdNumber, KraPin = d.KraPin, PhoneNumber = d.PhoneNumber, Email = d.Email, PostalAddress = d.PostalAddress,
        County = d.County, Occupation = d.Occupation, Employer = d.Employer, EmployeeNumber = d.EmployeeNumber,
    };
    private static NextOfKin Clone(NextOfKin k) => new() { Name = k.Name, Relationship = k.Relationship, PhoneNumber = k.PhoneNumber };
}

public sealed class MemberDirectory(MembersDbContext db) : IMemberDirectory
{
    public async Task<MemberSummary?> FindAsync(Guid memberId, CancellationToken ct)
        => Map(await db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.Id == memberId, ct));

    public async Task<MemberSummary?> FindByNumberAsync(string memberNumber, CancellationToken ct)
        => Map(await db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.MemberNumber == memberNumber, ct));

    public async Task<MemberSummary?> FindByPhoneAsync(string phoneNumber, CancellationToken ct)
        => Map(await db.Members.AsNoTracking().FirstOrDefaultAsync(m => m.Details.PhoneNumber == phoneNumber && m.KycStatus != KycStatus.Exited, ct));

    public async Task<bool> IsInGoodStandingAsync(Guid memberId, CancellationToken ct)
        => await db.Members.AsNoTracking().AnyAsync(m => m.Id == memberId && m.KycStatus == KycStatus.Verified, ct);

    private static MemberSummary? Map(Member? m) => m is null ? null
        : new MemberSummary(m.Id, m.MemberNumber, m.Details.FullName, m.Details.NationalIdNumber, m.Details.PhoneNumber, m.KycStatus, m.JoinedAt);
}
