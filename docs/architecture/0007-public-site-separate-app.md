# ADR 0007: Public tenant site is a separate Next.js app from the authenticated portal

## Status
Accepted

## Context
A per-SACCO public marketing/informational site (with an online membership application) has
a different rendering strategy (SSG/ISR for SEO and speed vs. the portal's dynamic,
auth-gated SSR), a different security posture (zero-auth, internet-facing, the most exposed
surface to abuse/scraping), and a different deploy cadence (marketing content changes far
more often than the transactional portal) than the authenticated portal.

## Decision
`frontend/public-site` is a separate Next.js app from `frontend/portal`, sharing the same
tenant-branding data source and the generated API client from `shared-contracts/`, but
deployed, cached, and CI'd independently. Its membership application form is
Turnstile-protected, verified server-side, and writes only a *pending application* to the
backend via a narrowly-scoped, rate-limited public endpoint — never a live member record.

## Consequences
- A vulnerability or misconfiguration on the public surface cannot directly compromise the
  authenticated portal's codebase or deployment.
- Two apps to maintain instead of one, but each is simpler and more cacheable for its actual
  purpose than a single app trying to do both would be.
- Public membership applications feed the existing KYC/staff-review workflow — no bypass of
  the verification process discussed for member onboarding.
