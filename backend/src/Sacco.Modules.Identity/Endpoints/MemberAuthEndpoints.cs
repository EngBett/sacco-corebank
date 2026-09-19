using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sacco.Modules.Identity.Application;
using Sacco.Modules.Identity.IdentityServer;
using Sacco.Shared.Auth;
using Sacco.Shared.Http;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Identity.Endpoints;

/// <param name="Phone">Kenyan mobile number — any shape <see cref="Domain.MemberLogin.NormalisePhone"/> accepts.</param>
/// <param name="Pin">The member's self-service PIN. Checked here (and again by <see cref="SaccoPasswordValidator"/> at
/// token time) so a wrong PIN never triggers an SMS send.</param>
/// <param name="DeviceId">Stable id the app generates once and keeps in secure storage — the unit device trust is
/// scoped to.</param>
public sealed record RequestOtpRequest(string Phone, string Pin, string DeviceId);
public sealed record RequestOtpResponse(bool OtpRequired, int ExpiresInSeconds);
public sealed record VerifyPinRequest(string Pin);
/// <param name="LockedOut">Too many wrong PINs: sign-in is locked too, for the same lockout period.</param>
public sealed record VerifyPinResponse(bool Valid, bool LockedOut);

/// <summary>
/// Pre-flight step for the native mobile app's phone+PIN sign-in (ADR 0008, 2026-09-16 update):
/// checks the PIN and, unless this device already completed OTP once, sends a fresh SMS code.
/// The actual sign-in still happens at <c>/connect/token</c> (grant_type=password, client_id=mobile)
/// with `device_id` and, when required, `otp` form fields — this endpoint only decides whether an
/// OTP is needed and gets the SMS moving; it never issues a token itself. Tenant comes from the
/// same `X-Tenant` header every other `/api/self/*` call already carries (resolved by
/// `TenantResolutionMiddleware` before this handler runs) — not a body field, since `/connect/token`
/// is the only endpoint in this flow with no ambient tenant context of its own.
/// </summary>
public sealed class MemberAuthEndpoints : IModuleEndpoints
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/self/auth/otp/request", async (RequestOtpRequest r, ITenantContext tenant, MemberLoginService memberLogins, MemberOtpService memberOtp, CancellationToken ct) =>
        {
            var member = await memberLogins.AuthenticateAsync(tenant.TenantId, r.Phone, r.Pin, ct);
            if (member is null) return Results.Unauthorized();

            var (otpRequired, expiresIn) = await memberOtp.RequestOtpIfNeededAsync(tenant.TenantId, member.MemberId, r.DeviceId, member.PhoneNumber, ct);
            return Results.Ok(new RequestOtpResponse(otpRequired, expiresIn));
        }).WithTags("Self-service").WithName("RequestMemberLoginOtp");

        // Signed-in member re-entering their PIN, e.g. before the app shows balances. A wrong PIN is an answer, not an
        // error — it returns 200 so the app's 401 handling (token refresh / sign-out) is never triggered by a typo.
        app.MapPost("/api/self/auth/pin/verify", async (VerifyPinRequest r, ICurrentUser user, MemberLoginService memberLogins, CancellationToken ct) =>
        {
            user.RequireMemberId();
            var result = await memberLogins.VerifyPinAsync(user.UserId, r.Pin, ct);
            return Results.Ok(new VerifyPinResponse(result == MemberLoginService.PinCheck.Valid, result == MemberLoginService.PinCheck.LockedOut));
        }).RequirePermission(Permissions.Self.ProfileView).WithTags("Self-service").WithName("VerifyMyPin");
    }
}
