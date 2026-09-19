using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sacco.Modules.Members.Application;
using Sacco.Modules.Members.Domain;
using Sacco.Modules.Members.Persistence;
using Sacco.Shared.Auth;
using Sacco.Shared.Http;
using Sacco.Shared.Members;
using Sacco.Shared.Savings;

namespace Sacco.Modules.Members.Endpoints;

public sealed record PersonalDetailsDto(string FirstName, string? MiddleName, string LastName, Gender Gender, DateOnly DateOfBirth, string NationalIdNumber, string? KraPin, string PhoneNumber, string? Email, string? PostalAddress, string County, string? Occupation, string? Employer, string? EmployeeNumber)
{
    public PersonalDetails ToDomain() => new()
    {
        FirstName = FirstName, MiddleName = MiddleName, LastName = LastName, Gender = Gender, DateOfBirth = DateOfBirth, NationalIdNumber = NationalIdNumber, KraPin = KraPin,
        PhoneNumber = PhoneNumber, Email = Email, PostalAddress = PostalAddress, County = County, Occupation = Occupation, Employer = Employer, EmployeeNumber = EmployeeNumber,
    };
    public static PersonalDetailsDto From(PersonalDetails d) => new(d.FirstName, d.MiddleName, d.LastName, d.Gender, d.DateOfBirth, d.NationalIdNumber, d.KraPin, d.PhoneNumber, d.Email, d.PostalAddress, d.County, d.Occupation, d.Employer, d.EmployeeNumber);
}
public sealed record NextOfKinDto(string Name, string Relationship, string PhoneNumber)
{
    public NextOfKin ToDomain() => new() { Name = Name, Relationship = Relationship, PhoneNumber = PhoneNumber };
    public static NextOfKinDto From(NextOfKin k) => new(k.Name, k.Relationship, k.PhoneNumber);
}
/// <param name="BranchId">Office that will serve the member; defaults to the registering officer's branch (ADR 0018).</param>
public sealed record SetMemberBranchRequest(Guid? BranchId);
public sealed record SaveMemberRequest(PersonalDetailsDto Details, NextOfKinDto NextOfKin, Guid? BranchId = null);
public sealed record KycDocumentDto(Guid Id, KycDocumentType Type, string FileReference, Guid UploadedByUserId, DateTimeOffset UploadedAt);
public sealed record AddDocumentRequest(KycDocumentType Type, string FileReference);
public sealed record ReasonRequest(string Reason);
public sealed record MemberResponse(Guid Id, string MemberNumber, string FullName, PersonalDetailsDto Details, NextOfKinDto NextOfKin, KycStatus KycStatus, MemberSource Source, DateOnly JoinedAt,
    Guid RegisteredByUserId, Guid? KycVerifiedByUserId, DateTimeOffset? KycVerifiedAt, string? KycRejectionReason, string? SuspensionReason, IReadOnlyList<KycDocumentDto> Documents,
    Guid? ExitRequestedByUserId, DateTimeOffset? ExitRequestedAt, string? ExitReason, Guid? ExitApprovedByUserId, DateTimeOffset? ExitedAt, string? ExitSettlementJson, bool SelfServiceEnabled, Guid? BranchId);
public sealed record ApproveExitRequest(ExitPayoutChannel Channel);
public sealed record EnableSelfServiceRequest(string Pin);
public sealed record MyProfileResponse(Guid Id, string MemberNumber, string FullName, string PhoneNumber, string? Email, KycStatus KycStatus, DateOnly JoinedAt, NextOfKinDto NextOfKin);
public sealed record MemberListItem(Guid Id, string MemberNumber, string FullName, string NationalIdNumber, string PhoneNumber, KycStatus KycStatus, DateOnly JoinedAt, Guid? BranchId);

public sealed record SubmitApplicationRequest(PersonalDetailsDto Details, NextOfKinDto NextOfKin, string TurnstileToken);
public sealed record ApplicationResponse(Guid Id, PersonalDetailsDto Details, NextOfKinDto NextOfKin, ApplicationStatus Status, DateTimeOffset SubmittedAt, string Channel, Guid? ReviewedByUserId, DateTimeOffset? ReviewedAt, string? ReviewNotes, Guid? CreatedMemberId);
public sealed record ReviewApplicationRequest(string? Notes);

public sealed class PublicApiOptions
{
    public const string SectionName = "PublicApi";
    /// <summary>Shared secret the public site's server sends in X-Public-Api-Key. Browsers never call these endpoints directly.</summary>
    public string ApiKey { get; set; } = "sandbox-public-api-key";
}

public sealed class MemberEndpoints : IModuleEndpoints
{
    public void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/members").WithTags("Members");

        g.MapGet("/", async (MembersDbContext db, string? search, KycStatus? status, Guid? branchId, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 200);
            var q = db.Members.AsNoTracking();
            if (status is KycStatus s) q = q.Where(m => m.KycStatus == s);
            if (branchId is Guid branch) q = q.Where(m => m.BranchId == branch);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                var pattern = $"%{term}%";
                q = q.Where(m => m.MemberNumber == term || m.Details.NationalIdNumber == term || m.Details.PhoneNumber == term
                                 || EF.Functions.ILike(m.Details.FirstName, pattern) || EF.Functions.ILike(m.Details.LastName, pattern));
            }
            var total = await q.CountAsync(ct);
            var items = await q.OrderBy(m => m.MemberNumber).Skip((page - 1) * pageSize).Take(pageSize)
                .Select(m => new MemberListItem(m.Id, m.MemberNumber, m.Details.FirstName + " " + m.Details.LastName, m.Details.NationalIdNumber, m.Details.PhoneNumber, m.KycStatus, m.JoinedAt, m.BranchId))
                .ToListAsync(ct);
            return TypedResults.Ok(new PagedResult<MemberListItem>(items, page, pageSize, total));
        }).RequirePermission(Permissions.Members.View).WithName("ListMembers");

        g.MapGet("/{id:guid}", async Task<Results<Ok<MemberResponse>, NotFound>> (Guid id, MembersDbContext db, MemberService members, CancellationToken ct) =>
        {
            var m = await db.Members.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return m is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(m, await members.IsSelfServiceEnabledAsync(id, ct)));
        }).RequirePermission(Permissions.Members.View).WithName("GetMember");

        // ---- Exit (maker-checker): request → settle loans from deposits, pay out balances, close accounts ----
        g.MapPost("/{id:guid}/exit/request", async (Guid id, ReasonRequest req, MemberService members, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await members.RequestExitAsync(id, req.Reason, user.UserId, ct))))
            .RequirePermission(Permissions.Members.Exit).WithName("RequestMemberExit");
        g.MapPost("/{id:guid}/exit/cancel", async (Guid id, ReasonRequest req, MemberService members, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await members.CancelExitAsync(id, req.Reason, user.UserId, ct))))
            .RequirePermission(Permissions.Members.ExitApprove).WithName("CancelMemberExit");
        g.MapPost("/{id:guid}/exit/approve", async (Guid id, ApproveExitRequest req, MemberService members, ICurrentUser user, CancellationToken ct) => TypedResults.Ok(ToResponse(await members.ApproveExitAsync(id, req.Channel, user.UserId, ct))))
            .RequirePermission(Permissions.Members.ExitApprove).WithName("ApproveMemberExit");

        // ---- Self-service login (phone + PIN) ----
        g.MapPost("/{id:guid}/self-service", async (Guid id, EnableSelfServiceRequest req, MemberService members, ICurrentUser user, CancellationToken ct) => { await members.EnableSelfServiceAsync(id, req.Pin, user.UserId, ct); return TypedResults.NoContent(); })
            .RequirePermission(Permissions.Members.SelfServiceManage).WithName("EnableMemberSelfService");
        g.MapDelete("/{id:guid}/self-service", async (Guid id, MemberService members, ICurrentUser user, CancellationToken ct) => { await members.DisableSelfServiceAsync(id, user.UserId, ct); return TypedResults.NoContent(); })
            .RequirePermission(Permissions.Members.SelfServiceManage).WithName("DisableMemberSelfService");

        app.MapGet("/api/self/profile", async (MembersDbContext db, ICurrentUser user, CancellationToken ct) =>
        {
            var m = await db.Members.AsNoTracking().FirstAsync(x => x.Id == user.RequireMemberId(), ct);
            return TypedResults.Ok(new MyProfileResponse(m.Id, m.MemberNumber, m.Details.FullName, m.Details.PhoneNumber, m.Details.Email, m.KycStatus, m.JoinedAt, NextOfKinDto.From(m.NextOfKin)));
        }).RequirePermission(Permissions.Self.ProfileView).WithTags("Self-service").WithName("GetMyProfile");

        g.MapGet("/by-number/{memberNumber}", async Task<Results<Ok<MemberResponse>, NotFound>> (string memberNumber, MembersDbContext db, CancellationToken ct) =>
        {
            var m = await db.Members.AsNoTracking().FirstOrDefaultAsync(x => x.MemberNumber == memberNumber, ct);
            return m is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(m));
        }).RequirePermission(Permissions.Members.View).WithName("GetMemberByNumber");

        g.MapPut("/{id:guid}/branch", async (Guid id, SetMemberBranchRequest req, MemberService members, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await members.SetBranchAsync(id, req.BranchId, user.UserId, ct))))
            .RequirePermission(Permissions.Members.Edit).WithName("SetMemberBranch");

        g.MapPost("/", async (SaveMemberRequest req, MemberService members, ICurrentUser user, CancellationToken ct) =>
        {
            var m = await members.RegisterAsync(null, null, req.Details.ToDomain(), req.NextOfKin.ToDomain(), MemberSource.StaffRegistered, null, user.UserId, ct, branchId: req.BranchId);
            return TypedResults.Created($"/api/members/{m.Id}", ToResponse(m));
        }).RequirePermission(Permissions.Members.Create).WithName("RegisterMember");

        g.MapPut("/{id:guid}", async (Guid id, SaveMemberRequest req, MemberService members, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await members.UpdateAsync(id, req.Details.ToDomain(), req.NextOfKin.ToDomain(), user.UserId, ct))))
            .RequirePermission(Permissions.Members.Edit).WithName("UpdateMember");

        g.MapPost("/{id:guid}/documents", async (Guid id, AddDocumentRequest req, MemberService members, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await members.AddDocumentAsync(id, req.Type, req.FileReference, user.UserId, ct))))
            .RequirePermission(Permissions.Members.Edit).WithName("AddKycDocument");

        g.MapPost("/{id:guid}/kyc/verify", async (Guid id, MemberService members, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await members.VerifyKycAsync(id, user.UserId, ct))))
            .RequirePermission(Permissions.Members.KycVerify).WithName("VerifyMemberKyc");

        g.MapPost("/{id:guid}/kyc/reject", async (Guid id, ReasonRequest req, MemberService members, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await members.RejectKycAsync(id, req.Reason, user.UserId, ct))))
            .RequirePermission(Permissions.Members.KycVerify).WithName("RejectMemberKyc");

        g.MapPost("/{id:guid}/suspend", async (Guid id, ReasonRequest req, MemberService members, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await members.SuspendAsync(id, req.Reason, user.UserId, ct))))
            .RequirePermission(Permissions.Members.Suspend).WithName("SuspendMember");

        g.MapPost("/{id:guid}/reinstate", async (Guid id, MemberService members, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await members.ReinstateAsync(id, user.UserId, ct))))
            .RequirePermission(Permissions.Members.Suspend).WithName("ReinstateMember");

        // ---- Applications (staff review) ----
        var apps = app.MapGroup("/api/members/applications").WithTags("Membership applications");

        apps.MapGet("/", async (MembersDbContext db, ApplicationStatus? status, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 200);
            var q = db.Applications.AsNoTracking();
            q = status is ApplicationStatus s ? q.Where(a => a.Status == s) : q;
            var total = await q.CountAsync(ct);
            var items = (await q.OrderBy(a => a.SubmittedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct)).Select(ToResponse).ToList();
            return TypedResults.Ok(new PagedResult<ApplicationResponse>(items, page, pageSize, total));
        }).RequirePermission(Permissions.Members.ApplicationsReview).WithName("ListMembershipApplications");

        apps.MapGet("/{id:guid}", async (Guid id, MemberService members, CancellationToken ct) => TypedResults.Ok(ToResponse(await members.GetApplicationAsync(id, ct))))
            .RequirePermission(Permissions.Members.ApplicationsReview).WithName("GetMembershipApplication");

        apps.MapPost("/{id:guid}/approve", async (Guid id, ReviewApplicationRequest req, MemberService members, ICurrentUser user, CancellationToken ct) =>
        {
            var (application, member) = await members.ApproveApplicationAsync(id, req.Notes, user.UserId, ct);
            return TypedResults.Ok(new { Application = ToResponse(application), Member = ToResponse(member) });
        }).RequirePermission(Permissions.Members.ApplicationsReview).WithName("ApproveMembershipApplication");

        apps.MapPost("/{id:guid}/reject", async (Guid id, ReasonRequest req, MemberService members, ICurrentUser user, CancellationToken ct) =>
            TypedResults.Ok(ToResponse(await members.RejectApplicationAsync(id, req.Reason, user.UserId, ct))))
            .RequirePermission(Permissions.Members.ApplicationsReview).WithName("RejectMembershipApplication");

        // ---- Public (called server-side by the public site; ADR 0007, non-negotiable #9) ----
        app.MapPost("/api/public/membership-applications", async (SubmitApplicationRequest req, HttpContext http, MemberService members, ITurnstileVerifier turnstile, IOptions<PublicApiOptions> publicApi, CancellationToken ct) =>
        {
            if (!http.Request.Headers.TryGetValue("X-Public-Api-Key", out var key) || key != publicApi.Value.ApiKey)
                return Results.Unauthorized();
            var remoteIp = http.Connection.RemoteIpAddress?.ToString();
            var passed = await turnstile.VerifyAsync(req.TurnstileToken, remoteIp, ct);
            var application = await members.SubmitApplicationAsync(req.Details.ToDomain(), req.NextOfKin.ToDomain(), "PublicSite", remoteIp, passed, ct);
            return Results.Created($"/api/members/applications/{application.Id}", new { application.Id, application.Status, application.SubmittedAt });
        }).RequireRateLimiting("public").WithTags("Public").WithName("SubmitMembershipApplication");
    }

    private static MemberResponse ToResponse(Member m, bool selfService = false) => new(m.Id, m.MemberNumber, m.Details.FullName, PersonalDetailsDto.From(m.Details), NextOfKinDto.From(m.NextOfKin), m.KycStatus, m.Source, m.JoinedAt,
        m.RegisteredByUserId, m.KycVerifiedByUserId, m.KycVerifiedAt, m.KycRejectionReason, m.SuspensionReason,
        m.Documents.Select(d => new KycDocumentDto(d.Id, d.Type, d.FileReference, d.UploadedByUserId, d.UploadedAt)).ToList(),
        m.ExitRequestedByUserId, m.ExitRequestedAt, m.ExitReason, m.ExitApprovedByUserId, m.ExitedAt, m.ExitSettlementJson, selfService, m.BranchId);

    private static ApplicationResponse ToResponse(MembershipApplication a) => new(a.Id, PersonalDetailsDto.From(a.Details), NextOfKinDto.From(a.NextOfKin), a.Status, a.SubmittedAt, a.Channel, a.ReviewedByUserId, a.ReviewedAt, a.ReviewNotes, a.CreatedMemberId);
}
