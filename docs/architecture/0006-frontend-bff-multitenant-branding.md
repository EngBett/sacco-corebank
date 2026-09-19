# ADR 0006: Next.js BFF layer with server-side session, tenant branding via Middleware

## Status
Accepted

## Context
The portal needs to talk to the .NET API without exposing tokens to browser JavaScript
(XSS risk, especially sensitive for a financial product). The platform is multi-tenant —
each licensed SACCO needs its own branding (logo, colors, name) without a separate codebase
or deployment per tenant.

## Decision
`frontend/portal` implements a BFF layer (Next.js Route Handlers/Server Actions): session
lives as an httpOnly secure cookie, the BFF holds and refreshes the access token server-side,
and proxies calls to the .NET API. The BFF stays a thin translation/aggregation layer — no
domain rules or authorization decisions live there. Tenant is resolved in Next.js Middleware
from subdomain/custom domain; tenant branding (colors, logo, name) is fetched server-side and
injected as CSS variables in the root layout for first-paint-correct theming, backed by
shadcn's CSS-variable-driven theming model.

## Consequences
- Tokens never reach browser JS; standard modern SPA/SSR security posture.
- One codebase serves every tenant; branding is data, not a deployment.
- Requires a tenant-branding data model and endpoint in the backend (logo in object storage,
  colors/name in the tenant record) before this can be fully implemented — sequence
  accordingly in the roadmap.

## Implementation note (2026-09-17): tenant logos
Object storage is not wired up yet, so a tenant's `LogoUrl` is either an absolute URL (a CDN, for example) or a
root-relative path to a file the API itself serves from `Sacco.Api/wwwroot/tenant-assets/{slug}/`. The demo tenant,
Icodeio SACCO, uses `/tenant-assets/demo/logo.png`. The API returns the value exactly as stored, and each client resolves a
relative path against an API address it can reach:
- the portal and public site use `API_BROWSER_URL`, in `getBranding`;
- the mobile app uses its API base URL;
- the identity sign-in page is served from the API origin, so the path works as it is;
- the nightly digest PDF inlines the file as a data URI.

The logo is a transparent-background PNG drawn for light backgrounds. On dark surfaces (the mobile app, and the portal
in dark mode) it is tinted white rather than stored twice. This matches the white transparent variant for a
single-colour logo; a multi-colour logo would show as a white silhouette. The logo is independent of the theme colours
(`PrimaryColor` etc.), which stay whatever the tenant chose.

`FaviconUrl` (added 2026-09-17) follows the same rules as `LogoUrl`. The demo tenant's favicon is
`/tenant-assets/demo/favicon.ico`. Both Next.js apps emit it from `generateMetadata`, and the identity sign-in page adds a
`<link rel="icon">`.

The identity server's sign-in page (`/account/login`) no longer asks staff to type their SACCO. The tenant comes from:
- the authorization request's tenant;
- the host or subdomain;
- or, outside production only, `Tenancy:DefaultTenantSlug`.

It is posted back as a hidden field. If none of these resolves a tenant, the page explains that and shows no form. The
page follows the shadcn `login-03` layout and uses Raleway, self-hosted from `Sacco.Api/wwwroot/fonts` (SIL OFL 1.1), so
signing in makes no third-party requests.

`LightModeLogoUrl` and `DarkModeLogoUrl` (optional; each falls back to `LogoUrl`) let a site that switches colour mode
use purpose-made logos instead of a tinted one. The public site renders both and shows one through Tailwind's `dark`
variant. The demo tenant uses Icodeio's dark logo on a transparent background for light mode and its white-on-dark logo
for dark mode. The public site is light-only today, so only the light-mode logo shows until a dark theme is added. The
portal and the sign-in pages keep `LogoUrl`.

The portal's admin branding form edits the stored, unresolved value. Moving logos to object storage later only means
storing absolute URLs; no client has to change.

