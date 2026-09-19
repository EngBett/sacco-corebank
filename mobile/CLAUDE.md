# CLAUDE.md — mobile

Scope not yet finalized — resolve this before starting Phase 7 (see
`docs/roadmap/ROADMAP.md`). Two very different products could live here, and they imply
different architectures:

1. **Member self-service app** — thin client against the same .NET API the portal uses
   (balance checks, mini-statements, loan applications, M-Pesa/Airtel Money top-up).
   Mostly online-only, similar trust boundary to `frontend/portal`.
2. **Field agent / loan officer app** — used for KYC data collection and loan appraisal in
   low-connectivity areas. Needs local-first storage and a sync/conflict-resolution strategy,
   which is a materially different architecture and deserves its own ADR before
   implementation starts.

Do not start building against an assumption here — confirm which (or both, as separate
apps) with the product owner and write the decision as an ADR in `docs/architecture/` first.


> **Update (Phase 7 prep):** a proposal is written up in `docs/architecture/0008-mobile-scope.md` — member self-service app first, field-agent app deferred. It is *Proposed*, not accepted; confirm with the product owner before any mobile code is written.

> **Update (2026-09-16):** accepted and built. The member self-service app is a Flutter app
> (feature-based clean architecture, Riverpod, `flutter_appauth` for OIDC PKCE) — see
> `README.md` in this directory for what it does and how to run it, and ADR 0008's
> "Implementation note (2026-09-16)" for the two backend endpoints it needed
> (`/api/self/withdrawals`, `/api/self/dividends`) and the deposit/withdrawal channel-scope
> decisions. The field-agent app is still deferred — no ADR for it exists yet.
