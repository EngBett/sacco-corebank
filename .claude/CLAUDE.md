# CLAUDE.md — SACCO Management Platform

This file is the top-level operating instructions for any AI agent (Fable, Claude Code, or
otherwise) working in this repository. Read this before touching any code. Module-specific
CLAUDE.md files (`backend/CLAUDE.md`, `frontend/CLAUDE.md`, `mobile/CLAUDE.md`,
`infrastructure/CLAUDE.md`) add detail on top of this — they never override it.

## What this system is

A core banking platform for Kenyan Deposit-Taking SACCOs, regulated by SASRA. It is licensed
to multiple SACCOs (multi-tenant). It handles real member money — savings, shares, loans,
withdrawals, disbursements. Correctness, auditability, and regulatory compliance outrank
feature velocity every time there is a conflict.

## Non-negotiables

These rules exist because they were deliberately decided (see `docs/architecture/`). Do not
silently work around them, and if a task seems to require violating one, stop and flag it
rather than improvising.

1. **Modular monolith, not microservices.** One .NET solution, one Postgres database, strict
   module boundaries enforced by project references. See ADR 0001. Peripheral, genuinely
   async concerns (payment provider callbacks, notifications) may run as separate processes
   communicating over the message bus — the core ledger/member/lending domain does not.
2. **FOSA/BOSA is a GL dimension, not two systems.** Every account and transaction carries a
   FOSA/BOSA tag. Reporting slices the same ledger by tag; it is never two parallel ledgers.
   See ADR 0002.
3. **Wolverine for messaging and sagas — not MassTransit.** MassTransit v9+ is commercial;
   Wolverine is MIT-licensed. Do not add a MassTransit dependency. See ADR 0003.
4. **Debits/credits use Postgres row locking (`SELECT ... FOR UPDATE` or atomic conditional
   `UPDATE`), never a distributed lock.** See ADR 0004.
5. **Every payment-provider write path is idempotent.** M-Pesa, Airtel Money, and bank
   webhooks can and will be delivered more than once. A unique constraint on the provider's
   transaction reference (or an explicit idempotency-key table) must make duplicate
   processing impossible, not just unlikely.
6. **Maker-checker on every money-moving approval.** The person who initiates a loan
   disbursement, GL adjustment, or dividend declaration cannot also approve it. This is
   enforced in code (`ApprovingUserId != InitiatedByUserId` at minimum), not by convention.
7. **Authorization is permission-based, not role-name based.** Roles are bundles of granular
   permissions (`loans.approve`, `members.kyc.verify`, etc.), checked via ASP.NET Core policy
   handlers. Never `if (user.Role == "...")` in application code.
8. **Every module ships with seed data and is demoable without production credentials.**
   See `docs/testing/seed-data-strategy.md`. If a PR adds a table, workflow, or provider
   integration with no seed data and no way to exercise it in demo mode, it is not done.
9. **A public-facing endpoint is never a shortcut into KYC-verified member status.** A public
   membership application (Turnstile-protected) creates a *pending application* for staff
   review — never a live member record.
10. **Business/domain rules live in the .NET backend, never in the Next.js BFF.** The BFF is
    a thin session/translation layer.

## Repository layout

See root `README.md` for the full table. In short: `backend/` (the modular monolith),
`frontend/portal/` (authenticated app + BFF), `frontend/public-site/` (marketing + membership
application, separate deployable), `mobile/`, `infrastructure/`, `docs/`.

## Before you start any task

1. Check `docs/roadmap/ROADMAP.md` — which phase does this task belong to, and what is that
   phase's demoable exit criterion?
2. Check `docs/architecture/` for any ADR touching the area you're about to change.
3. Check `docs/compliance/sasra-mapping.md` if the change touches ledger, loans, savings, or
   reporting — does this change need a corresponding update there?
4. If you are about to introduce a new external dependency, check its license. This project
   has already been burned once by a dependency going commercial mid-project (MassTransit) —
   prefer MIT/Apache-2.0 and note the license in the PR description.

## Definition of done

A task/PR is not done until:
- Code builds and passes CI (backend, frontend, or infra pipeline as applicable)
- Seed data exists to demo the change without any production credentials
- Unit tests cover the domain logic; integration tests cover the module boundary
- Relevant ADR/compliance doc updated if the change is architecturally or regulatorily
  significant
- The PR template checklist is honestly filled in, not rubber-stamped
