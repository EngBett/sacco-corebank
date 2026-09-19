using System.Net;

namespace Sacco.Modules.Identity.Endpoints;

/// <summary>The tenant as the account pages need it: resolved from the sign-in request, the host or configuration.</summary>
internal sealed record AccountBrand(Guid Id, string Slug, string Name, string Color, string? LogoUrl, string? FaviconUrl)
{
    public static AccountBrand? From((Guid Id, string Slug, string Name, string PrimaryColor, string? LogoUrl, string? FaviconUrl)? t) =>
        t is { } v ? new(v.Id, v.Slug, v.Name, v.PrimaryColor, v.LogoUrl, v.FaviconUrl) : null;
}

/// <summary>
/// Server-rendered account pages (sign-in, two-step verification, activation, password reset) in the shadcn <c>login-03</c>
/// layout: muted page, brand above a centred card, labelled fields, full-width button, fine print below. Raleway is
/// self-hosted from wwwroot/fonts so these pages make no third-party requests. No JavaScript.
/// </summary>
internal static class AccountPages
{
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");

    private static string Hidden(string name, string? value) => $"""<input type="hidden" name="{name}" value="{E(value)}">""";

    private static string Alert(string? error) => error is null ? "" : $"""<div class="alert" role="alert">{E(error)}</div>""";

    private static string Notice(string? message) => message is null ? "" : $"""<div class="notice" role="status">{E(message)}</div>""";

    private static string Header(string title, string description) =>
        $"""<div class="header"><h1>{E(title)}</h1><p class="description">{description}</p></div>""";

    private const string PasswordRules = "At least 10 characters, with letters and numbers.";

    public static string Login(AccountBrand? b, string returnUrl, string? userName, string? error, string? notice)
    {
        if (b is null) return NoTenant();
        var body = $$$"""
            {{{Header("Welcome back", $"Sign in to {E(b.Name)} with your staff account")}}}
            <form method="post" action="/account/login" autocomplete="on">
              {{{Hidden("tenant", b.Slug)}}}{{{Hidden("returnUrl", returnUrl)}}}
              {{{Notice(notice)}}}{{{Alert(error)}}}
              <div class="field">
                <label for="username">Username</label>
                <input id="username" name="username" value="{{{E(userName)}}}" autocomplete="username" required {{{(userName is null ? "autofocus" : "")}}}>
              </div>
              <div class="field">
                <div class="label-row"><label for="password">Password</label><a class="link" href="/account/forgot-password?tenant={{{Uri.EscapeDataString(b.Slug)}}}&amp;returnUrl={{{Uri.EscapeDataString(returnUrl)}}}">Forgot your password?</a></div>
                <input id="password" name="password" type="password" autocomplete="current-password" required {{{(userName is null ? "" : "autofocus")}}}>
              </div>
              <button type="submit">Sign in</button>
              <p class="description center">Don't have an account? Ask your system administrator.</p>
            </form>
            """;
        return Shell(b, "Sign in", body);
    }

    public static string NoTenant() => Shell(null, "Can't find your SACCO",
        Header("Can't find your SACCO", "This link isn't linked to a SACCO. Open the staff portal from your SACCO's web address and try again."));

    public static string MfaSetup(AccountBrand b, string qrSvg, string manualKey, string accountName, string? error) => Shell(b, "Set up two-step verification", $$$"""
        {{{Header("Set up two-step verification", "Every staff account is protected with a code from an authenticator app. You only do this once.")}}}
        <form method="post" action="/account/mfa/setup" autocomplete="off">
          {{{Alert(error)}}}
          <ol class="steps">
            <li><strong>Install an authenticator app</strong><span>Google Authenticator or Microsoft Authenticator, from your phone's app store.</span></li>
            <li><strong>Scan this QR code</strong><span>In the app, add an account and scan the code.</span>
              <div class="qr" role="img" aria-label="QR code for {{{E(accountName)}}}">{{{qrSvg}}}</div>
              <details><summary>Can't scan it?</summary><p class="description">Choose "enter a setup key" in the app and type this key (time-based):</p><code class="key">{{{E(manualKey)}}}</code></details>
            </li>
            <li><strong>Enter the 6-digit code</strong><span>The code the app now shows for {{{E(accountName)}}}.</span></li>
          </ol>
          <div class="field">
            <label for="code">Verification code</label>
            <input id="code" name="code" class="code" inputmode="numeric" pattern="[0-9 ]*" maxlength="7" autocomplete="one-time-code" required autofocus>
          </div>
          <button type="submit">Turn on two-step verification</button>
        </form>
        """);

    public static string RecoveryCodes(AccountBrand b, IReadOnlyList<string> codes, string continueUrl) => Shell(b, "Save your recovery codes", $$$"""
        {{{Header("Save your recovery codes", "Two-step verification is on. If you lose your phone, each of these codes lets you sign in once.")}}}
        <ul class="codes" aria-label="Recovery codes">{{{string.Concat(codes.Select(c => $"<li><code>{E(c)}</code></li>"))}}}</ul>
        <p class="description">Keep them somewhere safe and private, such as a password manager. They won't be shown again.</p>
        <a class="button" href="{{{E(continueUrl)}}}">I've saved my codes — continue</a>
        """);

    public static string MfaChallenge(AccountBrand b, string? error) => Shell(b, "Two-step verification", $$$"""
        {{{Header("Two-step verification", "Enter the 6-digit code from your authenticator app.")}}}
        <form method="post" action="/account/mfa" autocomplete="off">
          {{{Alert(error)}}}
          <div class="field">
            <label for="code">Verification code</label>
            <input id="code" name="code" class="code" inputmode="numeric" maxlength="11" autocomplete="one-time-code" required autofocus>
            <p class="description">Lost your phone? Enter one of your recovery codes instead.</p>
          </div>
          <button type="submit">Verify</button>
          <p class="description center"><a class="link inline" href="/account/login?tenant={{{Uri.EscapeDataString(b.Slug)}}}">Use a different account</a></p>
        </form>
        """);

    public static string ForgotPassword(AccountBrand b, string returnUrl, string? error) => Shell(b, "Forgot your password?", $$$"""
        {{{Header("Forgot your password?", "Enter your username or work email and we'll email you a link to choose a new password.")}}}
        <form method="post" action="/account/forgot-password">
          {{{Hidden("tenant", b.Slug)}}}{{{Hidden("returnUrl", returnUrl)}}}
          {{{Alert(error)}}}
          <div class="field">
            <label for="identifier">Username or email</label>
            <input id="identifier" name="identifier" autocomplete="username" required autofocus>
          </div>
          <button type="submit">Email me a reset link</button>
          <p class="description center"><a class="link inline" href="{{{E(LoginUrl(b, returnUrl))}}}">Back to sign in</a></p>
        </form>
        """);

    public static string ForgotPasswordSent(AccountBrand b, string returnUrl) => Shell(b, "Check your email", $$$"""
        {{{Header("Check your email", "If an account matches what you entered, we've sent a link to its email address. The link expires in 60 minutes.")}}}
        <p class="description center">Nothing arrived? Check your spam folder, or ask your system administrator to send a new link.</p>
        <a class="button secondary" href="{{{E(LoginUrl(b, returnUrl))}}}">Back to sign in</a>
        """);

    public static string SetPassword(AccountBrand b, string action, string token, string title, string description, string buttonLabel, string? userName, string? error) => Shell(b, title, $$$"""
        {{{Header(title, description)}}}
        <form method="post" action="/account/{{{action}}}" autocomplete="on">
          {{{Hidden("tenant", b.Slug)}}}{{{Hidden("token", token)}}}
          {{{Alert(error)}}}
          {{{(userName is null ? "" : $"""<input type="text" name="username" value="{E(userName)}" autocomplete="username" hidden readonly>""")}}}
          <div class="field">
            <label for="password">New password</label>
            <input id="password" name="password" type="password" autocomplete="new-password" minlength="10" required autofocus aria-describedby="rules">
            <p id="rules" class="description">{{{PasswordRules}}}</p>
          </div>
          <div class="field">
            <label for="confirm">Confirm new password</label>
            <input id="confirm" name="confirm" type="password" autocomplete="new-password" minlength="10" required>
          </div>
          <button type="submit">{{{E(buttonLabel)}}}</button>
        </form>
        """);

    public static string LinkInvalid(AccountBrand b, string title, string description) => Shell(b, title, $$$"""
        {{{Header(title, description)}}}
        <a class="button secondary" href="/account/forgot-password?tenant={{{Uri.EscapeDataString(b.Slug)}}}">Request a new link</a>
        """);

    public static string Done(AccountBrand b, string title, string description, string linkLabel, string link) => Shell(b, title, $$$"""
        {{{Header(title, description)}}}
        <a class="button" href="{{{E(link)}}}">{{{E(linkLabel)}}}</a>
        """);

    public static string LoggedOut() => Shell(null, "Signed out", Header("You have been signed out.", "You can close this window."));

    public static string LoginUrl(AccountBrand b, string? returnUrl) =>
        $"/account/login?tenant={Uri.EscapeDataString(b.Slug)}" + (string.IsNullOrEmpty(returnUrl) || returnUrl == "/" ? "" : $"&returnUrl={Uri.EscapeDataString(returnUrl)}");

    private static string Shell(AccountBrand? b, string title, string cardHtml)
    {
        var name = b?.Name ?? "SACCO Platform";
        var color = b?.Color ?? "#0f766e";
        // Served from the API origin, so root-relative tenant assets (/tenant-assets/...) load as-is.
        var brand = string.IsNullOrWhiteSpace(b?.LogoUrl)
            ? $"""<span class="mark" aria-hidden="true">{E(name[..1])}</span><span>{E(name)}</span>"""
            : $"""<img class="logo" src="{E(b!.LogoUrl)}" alt="{E(name)}">""";
        var favicon = string.IsNullOrWhiteSpace(b?.FaviconUrl) ? "" : $"""<link rel="icon" href="{E(b!.FaviconUrl)}">""";
        return $$$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="referrer" content="no-referrer"><meta name="robots" content="noindex">
            <title>{{{E(title)}}} · {{{E(name)}}}</title>{{{favicon}}}
            <style>
              @font-face{font-family:Raleway;src:url(/fonts/raleway-latin-wght-normal.woff2) format("woff2");font-weight:100 900;font-display:swap}
              :root{--brand:{{{E(color)}}};--bg:#f5f5f5;--card:#fff;--fg:#0a0a0a;--muted:#737373;--border:#e5e5e5;--subtle:#fafafa;--ring:color-mix(in srgb,var(--brand) 35%,transparent)}
              @media (prefers-color-scheme:dark){:root{--bg:#0a0a0a;--card:#171717;--fg:#fafafa;--muted:#a1a1a1;--border:#ffffff1a;--subtle:#262626}.logo{filter:brightness(0) invert(1)}}
              *{box-sizing:border-box}
              body{margin:0;min-height:100svh;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:1.5rem;padding:1.5rem;background:var(--bg);color:var(--fg);font-family:Raleway,ui-sans-serif,system-ui,sans-serif;font-variant-numeric:lining-nums;-webkit-font-smoothing:antialiased}
              .wrap{width:100%;max-width:24rem;display:flex;flex-direction:column;gap:1.5rem}
              .brand{display:flex;align-items:center;justify-content:center;gap:.5rem;font-weight:600}
              .logo{height:2.25rem;width:auto;max-width:100%}
              .mark{display:grid;place-items:center;width:1.5rem;height:1.5rem;border-radius:.375rem;background:var(--brand);color:#fff;font-size:.8rem}
              .card{background:var(--card);border:1px solid var(--border);border-radius:.75rem;box-shadow:0 1px 2px #0000000d;padding:1.5rem;display:flex;flex-direction:column;gap:1.25rem}
              .header{text-align:center}
              h1{font-size:1.25rem;font-weight:700;margin:0 0 .375rem}
              .description{margin:0;color:var(--muted);font-size:.875rem;line-height:1.45}
              .center{text-align:center}
              form{display:flex;flex-direction:column;gap:1.5rem}
              .field{display:flex;flex-direction:column;gap:.625rem}
              .label-row{display:flex;align-items:center}
              label{font-size:.875rem;font-weight:600}
              .link{margin-left:auto;font-size:.875rem;color:var(--fg);text-underline-offset:4px;text-decoration:none}
              .link.inline{margin-left:0;text-decoration:underline}
              .link:hover,.link:focus-visible{text-decoration:underline}
              input{height:2.25rem;width:100%;border:1px solid var(--border);border-radius:.375rem;background:transparent;color:var(--fg);padding:0 .75rem;font:inherit;font-size:.9375rem;box-shadow:0 1px 2px #0000000d}
              input.code{height:2.75rem;font-size:1.25rem;letter-spacing:.3em;text-align:center;font-variant-numeric:tabular-nums lining-nums}
              input:focus-visible{outline:none;border-color:var(--brand);box-shadow:0 0 0 3px var(--ring)}
              button,.button{display:flex;align-items:center;justify-content:center;height:2.25rem;width:100%;border:0;border-radius:.375rem;background:var(--brand);color:#fff;font:inherit;font-size:.875rem;font-weight:600;cursor:pointer;text-decoration:none}
              .button.secondary{background:transparent;color:var(--fg);border:1px solid var(--border)}
              button:hover,.button:hover{filter:brightness(1.08)}
              button:focus-visible,.button:focus-visible{outline:none;box-shadow:0 0 0 3px var(--ring)}
              .alert{border:1px solid #fca5a5;background:#fef2f2;color:#991b1b;border-radius:.375rem;padding:.5rem .75rem;font-size:.875rem}
              .notice{border:1px solid #99f6e4;background:#f0fdfa;color:#115e59;border-radius:.375rem;padding:.5rem .75rem;font-size:.875rem}
              @media (prefers-color-scheme:dark){.alert{background:#450a0a;border-color:#7f1d1d;color:#fecaca}.notice{background:#042f2e;border-color:#115e59;color:#99f6e4}}
              .steps{margin:0;padding:0;list-style:none;display:flex;flex-direction:column;gap:1rem;counter-reset:step}
              .steps li{counter-increment:step;display:flex;flex-direction:column;gap:.25rem;padding-left:2rem;position:relative;font-size:.875rem}
              .steps li::before{content:counter(step);position:absolute;left:0;top:0;width:1.375rem;height:1.375rem;border-radius:999px;background:var(--subtle);border:1px solid var(--border);display:grid;place-items:center;font-size:.75rem;font-weight:600}
              .steps span{color:var(--muted);line-height:1.45}
              .qr{margin:.5rem 0;width:11rem;height:11rem;padding:.5rem;background:#fff;border:1px solid var(--border);border-radius:.5rem}
              .qr svg{width:100%;height:100%;display:block}
              details{font-size:.875rem}summary{cursor:pointer;text-decoration:underline;text-underline-offset:4px}
              .key{display:block;margin-top:.5rem;padding:.5rem .625rem;background:var(--subtle);border:1px solid var(--border);border-radius:.375rem;font-size:.875rem;letter-spacing:.08em;word-break:break-all}
              .codes{margin:0;padding:0;list-style:none;display:grid;grid-template-columns:1fr 1fr;gap:.5rem}
              .codes code{display:block;text-align:center;padding:.4rem;background:var(--subtle);border:1px solid var(--border);border-radius:.375rem;font-size:.875rem}
              .fine{text-align:center;font-size:.75rem;color:var(--muted);padding:0 1.5rem;margin:0;text-wrap:balance}
            </style></head><body>
            <main class="wrap">
              <div class="brand">{{{brand}}}</div>
              <div class="card">{{{cardHtml}}}</div>
              <p class="fine">Access is for authorised {{{E(name)}}} staff only. Sign-ins are recorded for audit.</p>
            </main></body></html>
            """;
    }
}
