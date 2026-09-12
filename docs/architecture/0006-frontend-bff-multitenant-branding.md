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
