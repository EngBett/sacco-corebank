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
  `base-nova` style (Base UI: compose with `render={<Link … />}` rather than `asChild`; Base UI `Button` renders
  `type="button"` by default, so a `<Button>` inside a plain `<form>` needs an explicit `type="submit"` or it never submits). `next.config.ts` sets
  `turbopack.root` to the repo root so `@sacco/contracts` (../../shared-contracts) resolves.
- Types come from `shared-contracts/src/api.d.ts` (`Schemas["MemberResponse"]` etc.). Decimals arrive as
  `number | string`; use the `num`/`money` helpers, never raw arithmetic on them.
- Portal BFF: `src/lib/api.ts` (server-only fetch with bearer + `X-Tenant`, refresh, 401 → login, 403 → `/forbidden`),
  `src/app/actions.ts` (`apiAction`, a thin proxy for mutations), `src/app/api/auth/*` (openid-client v6 + iron-session).
  Mutations from client components go through `ActionButton` / `JsonForm`, which show the API's ProblemDetails verbatim.
- Public site: `src/app/api/apply/route.ts` verifies Turnstile (or skips when `TURNSTILE_SANDBOX=true`) and forwards
  with `X-Public-Api-Key`; nothing else on the site is authenticated.
- Portal shell and dashboard are the shadcn `dashboard-01` block (`npx shadcn@latest add dashboard-01`), adapted rather
  than used verbatim: `app-shell.tsx` (inset sidebar + `site-header.tsx`; `main` carries `@container/main`),
  `app-sidebar.tsx` (permission-filtered `NAV_MAIN`/`NAV_SECONDARY`, `sectionTitle()` for the header), `nav-user.tsx`
  (sign-out submits a hidden POST form), `section-cards.tsx` (`StatCard[]`), `chart-area-interactive.tsx` (monthly posted
  ledger value split FOSA/BOSA, built server-side from posted journals) and `data-table.tsx` (TanStack Table v9 over flat
  `WorkRow`s in tabbed `WorkView`s: approvals queue, recent payments, recent journals). The block's sample page, data.json,
  drag-to-reorder and drawer were removed; icons are `@phosphor-icons/react` (see `components.json`).
- Real-time notifications: `notification-bell.tsx` (header) loads the inbox through BFF routes under
  `src/app/api/notifications/*` (built on `src/lib/bff.ts`, which refreshes the token in place and returns the API's status
  instead of redirecting, because a browser `fetch()` cannot follow a sign-in redirect) and then subscribes to the API's
  SignalR hub with `@microsoft/signalr` (MIT). The browser gets a short-lived **hub ticket** from `/api/notifications/hub-ticket`,
  never the API token (ADR 0009); `API_BROWSER_URL` is the API origin as the browser sees it. Every push updates the badge
  and list and raises a sonner toast; `/notifications` lists everything with paging.
- Theme: `theme-provider.tsx` wraps the root layout in `next-themes` (`attribute="class"`, system default, `<html suppressHydrationWarning>`)
  and `mode-toggle.tsx` is the shadcn light/dark/system dropdown in the header. Brand colours already map into both `:root` and `.dark`.
- Base UI gotcha: `DropdownMenuLabel` must sit inside a `DropdownMenuGroup` (or radio group), otherwise the menu throws
  Base UI error #31 at open time and takes the page down; the production build does not catch it, so open every menu once.
- Credit scoring UI: `credit-score-card.tsx` (full factor breakdown on the loan page, `GradeBadge` in lists) and
  `scorecard-form.tsx` (client editor at `/loans/scoring`, shown read-only without `loans.scoring.manage`).
- Verify with `npm run lint && npm run build` in each app; both must stay clean.
