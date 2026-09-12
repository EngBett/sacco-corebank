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
