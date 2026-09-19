using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sacco.IntegrationTests.Infrastructure;
using Sacco.Modules.Identity.Application;
using Sacco.Modules.Identity.Application.Mfa;
using Sacco.Modules.Identity.Domain;
using Sacco.Modules.Identity.Endpoints;
using Sacco.Seed.Data;
using Sacco.Shared.Auth;
using Shouldly;

namespace Sacco.IntegrationTests.Identity;

/// <summary>
/// ADR 0016 end to end: a manager's invitation waits for a user administrator, the invitee activates from the emailed
/// link, sets up an authenticator at first sign-in, and every later sign-in needs a code; forgot-password never reveals
/// whether an account exists and its link works once.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class StaffOnboardingTests(PostgresFixture pg) : IDisposable
{
    private readonly ApiFactory _factory = new(pg.ConnectionString);
    public void Dispose() => _factory.Dispose();

    private const string NewPassword = "Welcome2026pass";

    private HttpClient Browser() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private HttpClient Admin => _factory.ClientAs(DemoTenant.Users.Admin, Permissions.Admin.UsersManage, Permissions.Admin.RolesManage);
    private HttpClient Manager => _factory.ClientAs(DemoTenant.Users.BranchManager, Permissions.Admin.UsersInvite);

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) => new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..14];

    private async Task<Guid> TellerRoleId()
    {
        var roles = await (await Admin.GetAsync("/api/admin/roles")).ReadAs<List<RoleResponse>>();
        return roles.Single(r => r.Name == "Teller").Id;
    }

    private static async Task<HttpResponseMessage> SignIn(HttpClient browser, string userName, string password) =>
        await browser.PostAsync("/account/login", Form(("tenant", DemoTenant.Slug), ("returnUrl", "/"), ("username", userName), ("password", password)));

    private static string CurrentCode(string manualKey, int offsetSteps = 0) =>
        Totp.Compute(Totp.Base32Decode(manualKey), Totp.StepAt(DateTimeOffset.UtcNow) + offsetSteps);

    [Fact]
    public async Task A_managers_invitation_needs_admin_approval_then_activation_and_a_mandatory_authenticator()
    {
        var userName = Unique("tel");
        var email = $"{userName}@example.co.ke";

        // A manager can see the roles to choose from, but not change them.
        (await Manager.GetAsync("/api/admin/roles")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Maker: the manager proposes. Nothing is emailed yet, and the manager can't approve it.
        var proposed = await (await Manager.PostAsJsonAsync("/api/admin/users/invitations",
            new InviteUserRequest(userName, email, "New Teller", null, [await TellerRoleId()]), HttpExtensions.JsonOptions)).ReadAs<StaffInvitationResponse>();
        proposed.Status.ShouldBe(StaffInvitationStatus.PendingApproval);
        _factory.Email.CountFor(email).ShouldBe(0);
        (await Manager.PostAsync($"/api/admin/users/invitations/{proposed.Id}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Checker: a user administrator approves; the account exists but can't sign in until activated.
        var approved = await (await Admin.PostAsync($"/api/admin/users/invitations/{proposed.Id}/approve", null)).ReadAs<StaffInvitationResponse>();
        approved.Status.ShouldBe(StaffInvitationStatus.Sent);
        var users = await (await Admin.GetAsync("/api/admin/users")).ReadAs<List<UserResponse>>();
        users.Single(u => u.UserName == userName).Status.ShouldBe(StaffUserStatus.Invited);
        (await SignIn(Browser(), userName, NewPassword)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Activation from the emailed link.
        var browser = Browser();
        var link = _factory.Email.LastLinkFor(email, "activate");
        var token = Regex.Match(link, "token=([^&]+)").Groups[1].Value;
        (await (await browser.GetAsync(link)).Content.ReadAsStringAsync()).ShouldContain("Activate your account");
        var mismatch = await browser.PostAsync("/account/activate", Form(("tenant", DemoTenant.Slug), ("token", Uri.UnescapeDataString(token)), ("password", NewPassword), ("confirm", "different")));
        mismatch.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var activated = await browser.PostAsync("/account/activate", Form(("tenant", DemoTenant.Slug), ("token", Uri.UnescapeDataString(token)), ("password", NewPassword), ("confirm", NewPassword)));
        activated.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        activated.Headers.Location!.ToString().ShouldContain("notice=activated");
        (await browser.GetAsync(link)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, "an activation link works once");
        (await (await Admin.GetAsync($"/api/admin/users/invitations?status=Accepted")).ReadAs<List<StaffInvitationResponse>>()).ShouldContain(i => i.Id == proposed.Id);

        // First sign-in: the password alone gives no session — set up the authenticator.
        var login = await SignIn(browser, userName, NewPassword);
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        login.Headers.Location!.ToString().ShouldBe("/account/mfa/setup");
        login.Headers.TryGetValues("Set-Cookie", out var cookies).ShouldBeTrue();
        cookies!.ShouldNotContain(c => c.StartsWith("idsrv"), "no IdentityServer session before two-step verification");

        var setupPage = await (await browser.GetAsync("/account/mfa/setup")).Content.ReadAsStringAsync();
        setupPage.ShouldContain("<svg");
        var manualKey = Regex.Match(setupPage, "<code class=\"key\">([A-Z2-7 ]+)</code>").Groups[1].Value;
        manualKey.ShouldNotBeEmpty();
        (await (await browser.GetAsync("/account/mfa/setup")).Content.ReadAsStringAsync()).ShouldContain(manualKey, Case.Sensitive, "reloading shows the same secret");

        var wrong = await browser.PostAsync("/account/mfa/setup", Form(("code", CurrentCode(manualKey, offsetSteps: 5))));
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var setupCode = CurrentCode(manualKey);
        var enrolled = await browser.PostAsync("/account/mfa/setup", Form(("code", setupCode)));
        enrolled.StatusCode.ShouldBe(HttpStatusCode.OK);
        enrolled.Headers.GetValues("Set-Cookie").ShouldContain(c => c.StartsWith("idsrv"), "signed in once the authenticator is confirmed");
        var recoveryCodes = Regex.Matches(await enrolled.Content.ReadAsStringAsync(), "<li><code>([a-z2-9]{5}-[a-z2-9]{5})</code></li>").Select(m => m.Groups[1].Value).ToList();
        recoveryCodes.Count.ShouldBe(StaffRecoveryCode.CodesPerUser);

        // Later sign-ins need a code; the set-up code can't be replayed, a recovery code works exactly once.
        var second = Browser();
        (await SignIn(second, userName, NewPassword)).Headers.Location!.ToString().ShouldBe("/account/mfa");
        (await second.PostAsync("/account/mfa", Form(("code", setupCode)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest, "the set-up code can't be replayed");
        var viaRecovery = await second.PostAsync("/account/mfa", Form(("code", recoveryCodes[0].ToUpperInvariant())));
        viaRecovery.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        viaRecovery.Headers.Location!.ToString().ShouldBe("/");

        var third = Browser();
        await SignIn(third, userName, NewPassword);
        (await third.PostAsync("/account/mfa", Form(("code", recoveryCodes[0])))).StatusCode.ShouldBe(HttpStatusCode.BadRequest, "recovery codes are single-use");

        // Lost phone: an administrator resets two-step verification and the user enrols again.
        var userId = users.Single(u => u.UserName == userName).Id;
        (await Admin.PostAsync($"/api/admin/users/{userId}/reset-mfa", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SignIn(Browser(), userName, NewPassword)).Headers.Location!.ToString().ShouldBe("/account/mfa/setup");
    }

    [Fact]
    public async Task Forgot_password_reveals_nothing_about_unknown_accounts_and_its_link_works_once()
    {
        var userName = Unique("fp");
        var email = $"{userName}@example.co.ke";
        using (var scope = _factory.TenantScope())
            await scope.ServiceProvider.GetRequiredService<UserService>().CreateAsync(null, userName, email, "Forgetful Clerk", null, "Original2026pass", [], DemoTenant.Users.Admin, mustChangePassword: false, CancellationToken.None);

        var browser = Browser();
        var unknown = await browser.PostAsync("/account/forgot-password", Form(("tenant", DemoTenant.Slug), ("returnUrl", "/"), ("identifier", "nobody-by-this-name")));
        var known = await browser.PostAsync("/account/forgot-password", Form(("tenant", DemoTenant.Slug), ("returnUrl", "/"), ("identifier", email.ToUpperInvariant())));
        unknown.StatusCode.ShouldBe(HttpStatusCode.OK);
        known.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await unknown.Content.ReadAsStringAsync()).ShouldBe(await known.Content.ReadAsStringAsync(), "the same answer whether or not the account exists");
        _factory.Email.CountFor(email).ShouldBe(1);

        await browser.PostAsync("/account/forgot-password", Form(("tenant", DemoTenant.Slug), ("returnUrl", "/"), ("identifier", userName)));
        _factory.Email.CountFor(email).ShouldBe(1, "a second request straight away is throttled");

        var link = _factory.Email.LastLinkFor(email, "reset-password");
        var token = Uri.UnescapeDataString(Regex.Match(link, "token=([^&]+)").Groups[1].Value);
        (await browser.GetAsync(link)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var weak = await browser.PostAsync("/account/reset-password", Form(("tenant", DemoTenant.Slug), ("token", token), ("password", "short"), ("confirm", "short")));
        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await weak.Content.ReadAsStringAsync()).ShouldContain("at least 10 characters");

        var reset = await browser.PostAsync("/account/reset-password", Form(("tenant", DemoTenant.Slug), ("token", token), ("password", NewPassword), ("confirm", NewPassword)));
        reset.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        reset.Headers.Location!.ToString().ShouldContain("notice=password-changed");
        _factory.Email.Sent.Last(m => m.To == email).Subject.ShouldContain("password was changed");
        (await browser.GetAsync(link)).StatusCode.ShouldBe(HttpStatusCode.BadRequest, "a reset link works once");

        (await SignIn(Browser(), userName, "Original2026pass")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await SignIn(Browser(), userName, NewPassword)).Headers.Location!.ToString().ShouldBe("/account/mfa/setup");

        // An administrator can send the link too, without ever knowing the password.
        var id = (await (await Admin.GetAsync("/api/admin/users")).ReadAs<List<UserResponse>>()).Single(u => u.UserName == userName).Id;
        (await Admin.PostAsync($"/api/admin/users/{id}/send-password-reset", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        _factory.Email.Sent.Last(m => m.To == email).Html.ShouldContain("an administrator has asked for");
    }

    [Fact]
    public async Task A_rejected_or_revoked_invitation_never_becomes_a_working_account()
    {
        var roleId = await TellerRoleId();
        var rejectedName = Unique("rej");
        var rejected = await (await Manager.PostAsJsonAsync("/api/admin/users/invitations",
            new InviteUserRequest(rejectedName, $"{rejectedName}@example.co.ke", "Not Hired", null, [roleId]), HttpExtensions.JsonOptions)).ReadAs<StaffInvitationResponse>();
        (await (await Admin.PostAsJsonAsync($"/api/admin/users/invitations/{rejected.Id}/reject", new RejectInvitationRequest("Position filled"), HttpExtensions.JsonOptions))
            .ReadAs<StaffInvitationResponse>()).Status.ShouldBe(StaffInvitationStatus.Rejected);
        _factory.Email.CountFor($"{rejectedName}@example.co.ke").ShouldBe(0);

        // A user administrator's own invitation goes out immediately; revoking it kills the link.
        var revokedName = Unique("rev");
        var direct = await (await Admin.PostAsJsonAsync("/api/admin/users/invitations",
            new InviteUserRequest(revokedName, $"{revokedName}@example.co.ke", "Changed Mind", null, [roleId]), HttpExtensions.JsonOptions)).ReadAs<StaffInvitationResponse>();
        direct.Status.ShouldBe(StaffInvitationStatus.Sent);
        var link = _factory.Email.LastLinkFor($"{revokedName}@example.co.ke", "activate");
        (await Admin.PostAsync($"/api/admin/users/invitations/{direct.Id}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Browser().GetAsync(link)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var duplicate = await Manager.PostAsJsonAsync("/api/admin/users/invitations",
            new InviteUserRequest("manager", "someone@example.co.ke", "Name Clash", null, [roleId]), HttpExtensions.JsonOptions);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
