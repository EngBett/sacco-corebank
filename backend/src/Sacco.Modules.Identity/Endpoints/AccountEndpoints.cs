using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Open.IdentityServer;
using Open.IdentityServer.Services;
using Sacco.Modules.Identity.Application;
using Sacco.Modules.Identity.Domain;
using Sacco.Modules.Identity.IdentityServer;
using Sacco.Shared.Audit;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Sacco.Shared.Http;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Identity.Endpoints;

/// <summary>
/// The interactive account pages IdentityServer redirects to during the authorization-code flow (ADR 0016):
/// sign-in, mandatory two-step verification for staff, invitation activation, and forgot/reset password.
/// Server-rendered HTML (<see cref="AccountPages"/>), tenant-branded, no JavaScript required.
/// </summary>
public sealed class AccountEndpoints(IHostEnvironment env, IConfiguration configuration) : IModuleEndpoints
{
    /// <summary>Rate-limiter policy applied to every credential-bearing POST (registered by the API host).</summary>
    public const string RateLimitPolicy = "account";

    private const string MfaCookie = "sacco.mfa";
    private static readonly TimeSpan MfaTicketLifetime = TimeSpan.FromMinutes(10);

    public void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/account").WithTags("Account (interactive login)").ExcludeFromDescription();

        // ---- Sign in ----
        g.MapGet("/login", async (HttpContext http, IIdentityServerInteractionService interaction, ITenantLookup tenants, string? returnUrl, string? tenant, string? notice, CancellationToken ct) =>
        {
            var brand = await BrandAsync(http, interaction, tenants, tenant, returnUrl, ct);
            return Html(AccountPages.Login(brand, returnUrl ?? "/", userName: null, error: null, NoticeText(notice)));
        });

        g.MapPost("/login", async (HttpContext http, IIdentityServerInteractionService interaction, ITenantLookup tenants, TenantContext tenantContext,
            UserService users, MemberLoginService memberLogins, IDataProtectionProvider dataProtection, IAuditLogger audit, IClock clock, CancellationToken ct) =>
        {
            var form = await http.Request.ReadFormAsync(ct);
            var returnUrl = form["returnUrl"].ToString();
            var userName = form["username"].ToString();
            var password = form["password"].ToString();
            var brand = AccountBrand.From(await FindTenantAsync(tenants, form["tenant"], ct));
            if (brand is null) return Html(AccountPages.NoTenant(), StatusCodes.Status400BadRequest);
            Bind(tenantContext, brand);

            var user = await users.AuthenticateAsync(brand.Id, userName, password, ct);
            if (user is not null)
            {
                // Staff never get a session from a password alone: set up or verify the authenticator first.
                WriteMfaTicket(http, dataProtection, new MfaTicket(user.Id, brand.Slug, returnUrl));
                return Results.Redirect(user.MfaEnabled ? "/account/mfa" : "/account/mfa/setup");
            }

            var member = await memberLogins.AuthenticateAsync(brand.Id, userName, password, ct);
            if (member is null)
                return Html(AccountPages.Login(brand, returnUrl, userName, "Invalid username or password.", notice: null), StatusCodes.Status401Unauthorized);

            await SignInAsync(http, member.LoginId, member.DisplayName, SubjectClaims.ForMember(brand.Id, brand.Slug, member.MemberId), clock, audit, "MemberLogin", ct);
            return Results.Redirect(SafeReturnUrl(interaction, returnUrl));
        }).RequireRateLimiting(RateLimitPolicy);

        // ---- Two-step verification: first-time set-up ----
        g.MapGet("/mfa/setup", async (HttpContext http, ITenantLookup tenants, TenantContext tenantContext, StaffMfaService mfa, IDataProtectionProvider dataProtection, CancellationToken ct) =>
        {
            var (ticket, brand) = await ReadMfaTicketAsync(http, dataProtection, tenants, tenantContext, ct);
            if (ticket is null || brand is null) return Results.Redirect("/account/login");
            var user = await mfa.GetUserAsync(ticket.UserId, ct);
            if (user.IsMfaEnabled) return Results.Redirect("/account/mfa");
            var enrollment = await mfa.BeginEnrollmentAsync(ticket.UserId, ct);
            return Html(AccountPages.MfaSetup(brand, enrollment.QrSvg, enrollment.ManualKey, enrollment.AccountName, error: null));
        });

        g.MapPost("/mfa/setup", async (HttpContext http, IIdentityServerInteractionService interaction, ITenantLookup tenants, TenantContext tenantContext,
            StaffMfaService mfa, IDataProtectionProvider dataProtection, IAuditLogger audit, IClock clock, CancellationToken ct) =>
        {
            var (ticket, brand) = await ReadMfaTicketAsync(http, dataProtection, tenants, tenantContext, ct);
            if (ticket is null || brand is null) return Results.Redirect("/account/login");
            var form = await http.Request.ReadFormAsync(ct);

            var codes = await mfa.ConfirmEnrollmentAsync(ticket.UserId, form["code"].ToString(), ct);
            var user = await mfa.GetUserAsync(ticket.UserId, ct);
            if (codes is null)
            {
                if (user.IsLockedOut(clock.UtcNow)) return LockedOut(http, brand);
                var enrollment = await mfa.BeginEnrollmentAsync(ticket.UserId, ct);
                return Html(AccountPages.MfaSetup(brand, enrollment.QrSvg, enrollment.ManualKey, enrollment.AccountName,
                    "That code didn't match. Check the app shows this account and try the newest code."), StatusCodes.Status400BadRequest);
            }

            ClearMfaTicket(http);
            await SignInAsync(http, user.Id, user.DisplayName, SubjectClaims.For(brand.Id, brand.Slug, user.BranchId), clock, audit, "StaffUser", ct);
            return Html(AccountPages.RecoveryCodes(brand, codes, SafeReturnUrl(interaction, ticket.ReturnUrl)));
        }).RequireRateLimiting(RateLimitPolicy);

        // ---- Two-step verification: every later sign-in ----
        g.MapGet("/mfa", async (HttpContext http, ITenantLookup tenants, TenantContext tenantContext, StaffMfaService mfa, IDataProtectionProvider dataProtection, CancellationToken ct) =>
        {
            var (ticket, brand) = await ReadMfaTicketAsync(http, dataProtection, tenants, tenantContext, ct);
            if (ticket is null || brand is null) return Results.Redirect("/account/login");
            var user = await mfa.GetUserAsync(ticket.UserId, ct);
            return user.IsMfaEnabled ? Html(AccountPages.MfaChallenge(brand, error: null)) : Results.Redirect("/account/mfa/setup");
        });

        g.MapPost("/mfa", async (HttpContext http, IIdentityServerInteractionService interaction, ITenantLookup tenants, TenantContext tenantContext,
            StaffMfaService mfa, IDataProtectionProvider dataProtection, IAuditLogger audit, IClock clock, CancellationToken ct) =>
        {
            var (ticket, brand) = await ReadMfaTicketAsync(http, dataProtection, tenants, tenantContext, ct);
            if (ticket is null || brand is null) return Results.Redirect("/account/login");
            var form = await http.Request.ReadFormAsync(ct);

            switch (await mfa.VerifyAsync(ticket.UserId, form["code"].ToString(), ct))
            {
                case MfaCheck.Valid:
                case MfaCheck.ValidRecoveryCode:
                    var user = await mfa.GetUserAsync(ticket.UserId, ct);
                    ClearMfaTicket(http);
                    await SignInAsync(http, user.Id, user.DisplayName, SubjectClaims.For(brand.Id, brand.Slug, user.BranchId), clock, audit, "StaffUser", ct);
                    return Results.Redirect(SafeReturnUrl(interaction, ticket.ReturnUrl));
                case MfaCheck.LockedOut:
                    return LockedOut(http, brand);
                default:
                    return Html(AccountPages.MfaChallenge(brand, "That code didn't work. Use the newest code from your app, or a recovery code."), StatusCodes.Status400BadRequest);
            }
        }).RequireRateLimiting(RateLimitPolicy);

        // ---- Invitation activation ----
        g.MapGet("/activate", async (HttpContext http, ITenantLookup tenants, TenantContext tenantContext, StaffAccountService accounts, string? tenant, string? token, CancellationToken ct) =>
        {
            var brand = AccountBrand.From(await FindTenantAsync(tenants, tenant, ct));
            if (brand is null) return Html(AccountPages.NoTenant(), StatusCodes.Status400BadRequest);
            Bind(tenantContext, brand);
            var user = await accounts.FindUserForTokenAsync(token ?? "", StaffTokenPurpose.Activation, ct);
            return user is null
                ? Html(InvalidActivation(brand), StatusCodes.Status400BadRequest)
                : Html(ActivatePage(brand, token!, user, error: null));
        });

        g.MapPost("/activate", async (HttpContext http, ITenantLookup tenants, TenantContext tenantContext, StaffAccountService accounts, CancellationToken ct) =>
        {
            var form = await http.Request.ReadFormAsync(ct);
            var brand = AccountBrand.From(await FindTenantAsync(tenants, form["tenant"], ct));
            if (brand is null) return Html(AccountPages.NoTenant(), StatusCodes.Status400BadRequest);
            Bind(tenantContext, brand);
            var token = form["token"].ToString();
            var user = await accounts.FindUserForTokenAsync(token, StaffTokenPurpose.Activation, ct);
            if (user is null) return Html(InvalidActivation(brand), StatusCodes.Status400BadRequest);

            var error = PasswordProblem(form["password"], form["confirm"]);
            if (error is null)
            {
                try
                {
                    if (await accounts.ActivateAsync(token, form["password"].ToString(), ct) == TokenCheck.Invalid)
                        return Html(InvalidActivation(brand), StatusCodes.Status400BadRequest);
                    return Results.Redirect(AccountPages.LoginUrl(brand, null) + "&notice=activated");
                }
                catch (DomainRuleException ex) { error = ex.Message; }
            }
            return Html(ActivatePage(brand, token, user, error), StatusCodes.Status400BadRequest);
        }).RequireRateLimiting(RateLimitPolicy);

        // ---- Forgot / reset password ----
        g.MapGet("/forgot-password", async (HttpContext http, IIdentityServerInteractionService interaction, ITenantLookup tenants, string? tenant, string? returnUrl, CancellationToken ct) =>
        {
            var brand = await BrandAsync(http, interaction, tenants, tenant, returnUrl, ct);
            return brand is null ? Html(AccountPages.NoTenant(), StatusCodes.Status400BadRequest) : Html(AccountPages.ForgotPassword(brand, returnUrl ?? "/", error: null));
        });

        g.MapPost("/forgot-password", async (HttpContext http, ITenantLookup tenants, TenantContext tenantContext, StaffAccountService accounts, CancellationToken ct) =>
        {
            var form = await http.Request.ReadFormAsync(ct);
            var brand = AccountBrand.From(await FindTenantAsync(tenants, form["tenant"], ct));
            if (brand is null) return Html(AccountPages.NoTenant(), StatusCodes.Status400BadRequest);
            Bind(tenantContext, brand);
            var returnUrl = form["returnUrl"].ToString();
            var identifier = form["identifier"].ToString();
            if (string.IsNullOrWhiteSpace(identifier))
                return Html(AccountPages.ForgotPassword(brand, returnUrl, "Enter your username or email."), StatusCodes.Status400BadRequest);

            // Same response whether or not the account exists (no user enumeration).
            await accounts.RequestPasswordResetAsync(identifier, ct);
            return Html(AccountPages.ForgotPasswordSent(brand, returnUrl));
        }).RequireRateLimiting(RateLimitPolicy);

        g.MapGet("/reset-password", async (HttpContext http, ITenantLookup tenants, TenantContext tenantContext, StaffAccountService accounts, string? tenant, string? token, CancellationToken ct) =>
        {
            var brand = AccountBrand.From(await FindTenantAsync(tenants, tenant, ct));
            if (brand is null) return Html(AccountPages.NoTenant(), StatusCodes.Status400BadRequest);
            Bind(tenantContext, brand);
            var user = await accounts.FindUserForTokenAsync(token ?? "", StaffTokenPurpose.PasswordReset, ct);
            return user is null ? Html(InvalidReset(brand), StatusCodes.Status400BadRequest) : Html(ResetPage(brand, token!, user, error: null));
        });

        g.MapPost("/reset-password", async (HttpContext http, ITenantLookup tenants, TenantContext tenantContext, StaffAccountService accounts, CancellationToken ct) =>
        {
            var form = await http.Request.ReadFormAsync(ct);
            var brand = AccountBrand.From(await FindTenantAsync(tenants, form["tenant"], ct));
            if (brand is null) return Html(AccountPages.NoTenant(), StatusCodes.Status400BadRequest);
            Bind(tenantContext, brand);
            var token = form["token"].ToString();
            var user = await accounts.FindUserForTokenAsync(token, StaffTokenPurpose.PasswordReset, ct);
            if (user is null) return Html(InvalidReset(brand), StatusCodes.Status400BadRequest);

            var error = PasswordProblem(form["password"], form["confirm"]);
            if (error is null)
            {
                try
                {
                    if (await accounts.ResetPasswordAsync(token, form["password"].ToString(), ct) == TokenCheck.Invalid)
                        return Html(InvalidReset(brand), StatusCodes.Status400BadRequest);
                    return Results.Redirect(AccountPages.LoginUrl(brand, null) + "&notice=password-changed");
                }
                catch (DomainRuleException ex) { error = ex.Message; }
            }
            return Html(ResetPage(brand, token, user, error), StatusCodes.Status400BadRequest);
        }).RequireRateLimiting(RateLimitPolicy);

        // ---- Sign out ----
        g.MapGet("/logout", async (HttpContext http, IIdentityServerInteractionService interaction, ICurrentUser user, IAuditLogger audit, string? logoutId, CancellationToken ct) =>
        {
            if (user.IsAuthenticated)
                await audit.RecordAsync(new AuditEvent("identity.signout", "StaffUser", user.UserId.ToString(), user.UserId), ct);
            await http.SignOutAsync(IdentityServerConstants.DefaultCookieAuthenticationScheme);
            ClearMfaTicket(http);
            var ctx = logoutId is null ? null : await interaction.GetLogoutContextAsync(logoutId);
            var redirect = ctx?.PostLogoutRedirectUri;
            return string.IsNullOrEmpty(redirect) ? Html(AccountPages.LoggedOut()) : Results.Redirect(redirect);
        });
    }

    private static IResult Html(string html, int statusCode = StatusCodes.Status200OK) => Results.Content(html, "text/html", statusCode: statusCode);

    private static string? NoticeText(string? notice) => notice switch
    {
        "activated" => "Your account is active. Sign in to set up two-step verification.",
        "password-changed" => "Your password was changed. Sign in with your new password.",
        "locked" => null,
        _ => null,
    };

    private static string ActivatePage(AccountBrand brand, string token, StaffUser user, string? error) => AccountPages.SetPassword(brand, "activate", token,
        "Activate your account", $"Welcome, {WebUtility.HtmlEncode(user.DisplayName)}. Your username is <strong>{WebUtility.HtmlEncode(user.UserName)}</strong>. Choose a password to finish setting up your account.",
        "Activate account", user.UserName, error);

    private static string ResetPage(AccountBrand brand, string token, StaffUser user, string? error) => AccountPages.SetPassword(brand, "reset-password", token,
        "Choose a new password", $"Choose a new password for <strong>{WebUtility.HtmlEncode(user.UserName)}</strong>.", "Change password", user.UserName, error);

    private static string InvalidActivation(AccountBrand brand) => AccountPages.Done(brand, "This activation link has expired",
        "Activation links work once and expire after 72 hours. Ask your system administrator to resend your invitation.", "Go to sign in", AccountPages.LoginUrl(brand, null));

    private static string InvalidReset(AccountBrand brand) => AccountPages.LinkInvalid(brand, "This reset link has expired",
        "Reset links work once and expire after 60 minutes. Request a new one below.");

    private static IResult LockedOut(HttpContext http, AccountBrand brand)
    {
        ClearMfaTicket(http);
        return Html(AccountPages.Done(brand, "Too many attempts", "For your security, sign-in is paused for 15 minutes. Try again later, or ask your system administrator for help.",
            "Back to sign in", AccountPages.LoginUrl(brand, null)), StatusCodes.Status429TooManyRequests);
    }

    private static string? PasswordProblem(string? password, string? confirm)
    {
        if (string.IsNullOrEmpty(password)) return "Enter a new password.";
        if (password != confirm) return "The two passwords don't match.";
        return null;
    }

    private async Task<AccountBrand?> BrandAsync(HttpContext http, IIdentityServerInteractionService interaction, ITenantLookup tenants, string? tenant, string? returnUrl, CancellationToken ct)
    {
        var slug = await ResolveTenantSlug(interaction, tenant, returnUrl, http, ct);
        return slug is null ? null : AccountBrand.From(await tenants.FindBySlugAsync(slug, ct));
    }

    private static async Task<(Guid Id, string Slug, string Name, string PrimaryColor, string? LogoUrl, string? FaviconUrl)?> FindTenantAsync(ITenantLookup tenants, string? slug, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(slug) ? null : await tenants.FindBySlugAsync(slug, ct);

    /// <summary>/account is a tenant-agnostic route, so the page binds the tenant it resolved before touching tenant data.</summary>
    private static void Bind(TenantContext tenantContext, AccountBrand brand)
    {
        if (!tenantContext.HasTenant) tenantContext.Set(brand.Id, brand.Slug);
        else if (tenantContext.TenantId != brand.Id) throw new ForbiddenException("This page belongs to a different SACCO than the current request.");
    }

    private static async Task SignInAsync(HttpContext http, Guid subjectId, string displayName, IEnumerable<System.Security.Claims.Claim> claims, IClock clock,
        IAuditLogger? audit = null, string? kind = null, CancellationToken ct = default)
    {
        var principal = new IdentityServerUser(subjectId.ToString())
        {
            DisplayName = displayName,
            AuthenticationTime = clock.UtcNow.UtcDateTime,
            AdditionalClaims = claims.ToList(),
        }.CreatePrincipal();
        await http.SignInAsync(IdentityServerConstants.DefaultCookieAuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = false });
        if (audit is not null)
            await audit.RecordAsync(new AuditEvent("identity.signin.succeeded", kind ?? "StaffUser", subjectId.ToString(), subjectId,
                AuditDetails.New().With("displayName", displayName).ToJson()), ct);
    }

    /// <summary>Only same-site paths or IdentityServer-issued return URLs; never "//evil.example".</summary>
    private static string SafeReturnUrl(IIdentityServerInteractionService interaction, string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl)) return "/";
        if (interaction.IsValidReturnUrl(returnUrl)) return returnUrl;
        return returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\") ? returnUrl : "/";
    }

    // ---- Partial sign-in between the password and the authenticator code ----

    private sealed record MfaTicket(Guid UserId, string TenantSlug, string ReturnUrl);

    private static ITimeLimitedDataProtector Protector(IDataProtectionProvider provider) =>
        provider.CreateProtector("Sacco.Identity.MfaTicket.v1").ToTimeLimitedDataProtector();

    private static void WriteMfaTicket(HttpContext http, IDataProtectionProvider provider, MfaTicket ticket)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(ticket);
        http.Response.Cookies.Append(MfaCookie, Protector(provider).Protect(payload, MfaTicketLifetime), new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/account",
            MaxAge = MfaTicketLifetime,
        });
    }

    private static void ClearMfaTicket(HttpContext http) => http.Response.Cookies.Delete(MfaCookie, new CookieOptions { Path = "/account" });

    private static async Task<(MfaTicket? Ticket, AccountBrand? Brand)> ReadMfaTicketAsync(HttpContext http, IDataProtectionProvider provider, ITenantLookup tenants, TenantContext tenantContext, CancellationToken ct)
    {
        if (!http.Request.Cookies.TryGetValue(MfaCookie, out var raw) || string.IsNullOrEmpty(raw)) return (null, null);
        MfaTicket? ticket;
        try { ticket = System.Text.Json.JsonSerializer.Deserialize<MfaTicket>(Protector(provider).Unprotect(raw)); }
        catch (System.Security.Cryptography.CryptographicException) { return (null, null); } // expired or tampered
        if (ticket is null) return (null, null);
        var brand = AccountBrand.From(await tenants.FindBySlugAsync(ticket.TenantSlug, ct));
        if (brand is null) return (null, null);
        Bind(tenantContext, brand);
        return (ticket, brand);
    }

    private async Task<string?> ResolveTenantSlug(IIdentityServerInteractionService interaction, string? tenant, string? returnUrl, HttpContext http, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(tenant)) return tenant;
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            var ctx = await interaction.GetAuthorizationContextAsync(returnUrl);
            if (!string.IsNullOrWhiteSpace(ctx?.Tenant)) return ctx.Tenant;
        }
        if (http.Request.Headers.TryGetValue("X-Tenant", out var h) && !string.IsNullOrWhiteSpace(h)) return h.ToString();
        var host = http.Request.Host.Host;
        if (!string.IsNullOrEmpty(host) && host != "localhost" && !IPAddress.TryParse(host, out _)) return host.Split('.')[0];
        return env.IsProduction() ? null : configuration["Tenancy:DefaultTenantSlug"];
    }
}
