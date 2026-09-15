using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Open.IdentityServer;
using Open.IdentityServer.Services;
using Sacco.Modules.Identity.Application;
using Sacco.Modules.Identity.IdentityServer;
using Sacco.Shared.Http;
using Sacco.Shared.Time;

namespace Sacco.Modules.Identity.Endpoints;

/// <summary>
/// The interactive login/logout pages IdentityServer redirects to during the authorization-code
/// flow. Server-rendered HTML, tenant-branded, no JavaScript required.
/// </summary>
public sealed class AccountEndpoints(IHostEnvironment env, IConfiguration configuration) : IModuleEndpoints
{
    public void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/account").WithTags("Account (interactive login)").ExcludeFromDescription();

        g.MapGet("/login", async (HttpContext http, IIdentityServerInteractionService interaction, ITenantLookup tenants, string? returnUrl, string? tenant, CancellationToken ct) =>
        {
            var slug = await ResolveTenantSlug(interaction, tenant, returnUrl, http, ct);
            var t = slug is null ? null : await tenants.FindBySlugAsync(slug, ct);
            return Results.Content(LoginPage(t?.Name ?? "SACCO Platform", t?.PrimaryColor ?? "#0f766e", slug ?? "", returnUrl ?? "/", null), "text/html");
        });

        g.MapPost("/login", async (HttpContext http, IIdentityServerInteractionService interaction, ITenantLookup tenants, UserService users, MemberLoginService memberLogins, IClock clock, CancellationToken ct) =>
        {
            var form = await http.Request.ReadFormAsync(ct);
            var slug = form["tenant"].ToString();
            var returnUrl = form["returnUrl"].ToString();
            var userName = form["username"].ToString();
            var password = form["password"].ToString();

            var t = string.IsNullOrWhiteSpace(slug) ? null : await tenants.FindBySlugAsync(slug, ct);
            var user = t is null ? null : await users.AuthenticateAsync(t.Value.Id, userName, password, ct);
            var member = user is null || t is null ? null : null as AuthenticatedMember;
            if (user is null && t is not null) member = await memberLogins.AuthenticateAsync(t.Value.Id, userName, password, ct);
            if (user is null && member is null)
                return Results.Content(LoginPage(t?.Name ?? "SACCO Platform", t?.PrimaryColor ?? "#0f766e", slug, returnUrl, "Invalid username or password."), "text/html", statusCode: 401);

            var principal = new IdentityServerUser((user?.Id ?? member!.LoginId).ToString())
            {
                DisplayName = user?.DisplayName ?? member!.DisplayName,
                AuthenticationTime = clock.UtcNow.UtcDateTime,
                AdditionalClaims = (user is not null ? SubjectClaims.For(t!.Value.Id, t.Value.Slug) : SubjectClaims.ForMember(t!.Value.Id, t.Value.Slug, member!.MemberId)).ToList(),
            }.CreatePrincipal();
            await http.SignInAsync(IdentityServerConstants.DefaultCookieAuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = false });

            if (!string.IsNullOrEmpty(returnUrl) && (interaction.IsValidReturnUrl(returnUrl) || returnUrl.StartsWith('/')))
                return Results.Redirect(returnUrl);
            return Results.Redirect("/");
        });

        g.MapGet("/logout", async (HttpContext http, IIdentityServerInteractionService interaction, string? logoutId) =>
        {
            await http.SignOutAsync(IdentityServerConstants.DefaultCookieAuthenticationScheme);
            var ctx = logoutId is null ? null : await interaction.GetLogoutContextAsync(logoutId);
            var redirect = ctx?.PostLogoutRedirectUri;
            return string.IsNullOrEmpty(redirect) ? Results.Content(LoggedOutPage(), "text/html") : Results.Redirect(redirect);
        });
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

    private static string LoginPage(string tenantName, string color, string slug, string returnUrl, string? error) => $$"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Sign in · {{WebUtility.HtmlEncode(tenantName)}}</title>
        <style>
          :root{--brand:{{color}};}
          body{margin:0;font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;background:#f6f7f9;color:#111827;display:grid;place-items:center;min-height:100vh}
          .card{background:#fff;border-radius:16px;box-shadow:0 10px 30px rgba(0,0,0,.06);padding:2.25rem;width:min(92vw,400px)}
          h1{font-size:1.25rem;margin:0 0 .25rem}p.sub{margin:0 0 1.5rem;color:#6b7280;font-size:.9rem}
          label{display:block;font-size:.85rem;font-weight:600;margin:.9rem 0 .35rem}
          input{width:100%;box-sizing:border-box;border:1px solid #d1d5db;border-radius:10px;padding:.65rem .8rem;font-size:1rem}
          input:focus{outline:2px solid var(--brand);border-color:transparent}
          button{margin-top:1.4rem;width:100%;border:0;border-radius:10px;padding:.75rem;background:var(--brand);color:#fff;font-weight:600;font-size:1rem;cursor:pointer}
          .err{background:#fef2f2;color:#991b1b;border-radius:10px;padding:.6rem .8rem;font-size:.9rem;margin-top:1rem}
          .brand{display:flex;align-items:center;gap:.6rem;margin-bottom:1.25rem}.dot{width:14px;height:14px;border-radius:50%;background:var(--brand)}
        </style></head><body>
        <form class="card" method="post" action="/account/login" autocomplete="off">
          <div class="brand"><span class="dot"></span><strong>{{WebUtility.HtmlEncode(tenantName)}}</strong></div>
          <h1>Staff sign in</h1><p class="sub">Use your SACCO staff credentials.</p>
          <input type="hidden" name="returnUrl" value="{{WebUtility.HtmlEncode(returnUrl)}}">
          <label for="tenant">SACCO</label><input id="tenant" name="tenant" value="{{WebUtility.HtmlEncode(slug)}}" required>
          <label for="username">Username</label><input id="username" name="username" required autofocus>
          <label for="password">Password</label><input id="password" name="password" type="password" required>
          {{(error is null ? "" : $"<div class=\"err\">{WebUtility.HtmlEncode(error)}</div>")}}
          <button type="submit">Sign in</button>
        </form></body></html>
        """;

    private static string LoggedOutPage() => """
        <!doctype html><html lang="en"><head><meta charset="utf-8"><title>Signed out</title>
        <style>body{font-family:system-ui,sans-serif;display:grid;place-items:center;min-height:100vh;background:#f6f7f9;color:#111827}</style></head>
        <body><div><h1>You have been signed out.</h1></div></body></html>
        """;
}
