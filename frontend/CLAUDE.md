# CLAUDE.md — frontend

Read `../.claude/CLAUDE.md` first. Two separate Next.js apps live here — read the section
below that matches what you're working on. Full UI/UX guidelines are in
`DESIGN_GUIDELINES.md` in this same directory; follow them for every screen in both apps.

## frontend/portal — authenticated staff/member app

- App Router, with a BFF layer implemented as Next.js Route Handlers / Server Actions.
- Session lives server-side as an httpOnly, secure cookie. The access token from
  Open.IdentityServer never reaches browser JavaScript.
- The BFF proxies to the .NET API and may reshape responses for a given page, but never
  makes an authorization decision or encodes a domain rule — the backend is the source of
  truth for "is this allowed."
- Sidebar layout for dashboards; table + filters for admin/back-office screens; forms use
  shadcn `Form` + validation matching the backend's rules (validated again server-side
  regardless — client validation is UX, not security).
- Theme (colors, logo, org name) is resolved server-side per tenant and injected as CSS
  variables in the root layout — no flash of default branding.

## frontend/public-site — public, tenant-branded marketing site

- Statically generated / ISR wherever possible — this is the SEO- and speed-sensitive
  surface, and it's the one most exposed to the open internet.
- Tenant resolved in Next.js Middleware from subdomain or custom domain, before any page
  renders.
- Membership application form is Turnstile-protected; verification happens server-side in a
  Route Handler that calls Cloudflare `siteverify`, then forwards to a narrowly-scoped public
  .NET endpoint (rate-limited, duplicate-submission checked). This creates a pending
  application only — never a live member record.
- No session/auth complexity here beyond what the application form needs.

## Shared

Generate the API client from `../shared-contracts/` (OpenAPI-derived). Do not hand-write
fetch calls against guessed endpoint shapes.


## Implementation notes (Phase 7)

- Both apps are Next.js 16 (App Router, Turbopack, `src/proxy.ts` instead of middleware), Tailwind v4 and the shadcn
  `base-nova` style (Base UI: compose with `render={<Link … />}` rather than `asChild`). `next.config.ts` sets
  `turbopack.root` to the repo root so `@sacco/contracts` (../../shared-contracts) resolves.
- Types come from `shared-contracts/src/api.d.ts` (`Schemas["MemberResponse"]` etc.). Decimals arrive as
  `number | string`; use the `num`/`money` helpers, never raw arithmetic on them.
- Portal BFF: `src/lib/api.ts` (server-only fetch with bearer + `X-Tenant`, refresh, 401 → login, 403 → `/forbidden`),
  `src/app/actions.ts` (`apiAction`, a thin proxy for mutations), `src/app/api/auth/*` (openid-client v6 + iron-session).
  Mutations from client components go through `ActionButton` / `JsonForm`, which show the API's ProblemDetails verbatim.
- Public site: `src/app/api/apply/route.ts` verifies Turnstile (or skips when `TURNSTILE_SANDBOX=true`) and forwards
  with `X-Public-Api-Key`; nothing else on the site is authenticated.
- Verify with `npm run lint && npm run build` in each app; both must stay clean.
