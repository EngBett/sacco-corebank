# SACCO Management Platform

A multi-tenant, SASRA-compliant core banking platform for Deposit-Taking SACCOs in Kenya.
Covers KYC/member management, savings & shares (BOSA), on-demand transactions (FOSA),
loan lifecycle with guarantors, GL/ledger, regulatory reporting, and mobile money / bank
payment integration.

## Start here

1. Read `.claude/CLAUDE.md` — the operating instructions for any AI agent (or human) working
   in this repo. It is the single source of truth for conventions, non-negotiables, and how
   the rest of this documentation fits together.
2. Read `docs/roadmap/ROADMAP.md` — the phased build plan. Work phase by phase; each phase
   has an explicit, demoable, testable exit criterion.
3. Read every file in `docs/architecture/` (ADRs) before making any structural decision —
   they capture *why*, not just *what*, and should not be silently contradicted.
4. Read `docs/testing/seed-data-strategy.md` before writing any module — every module ships
   with seed data from day one, not as an afterthought.

## Golden rule for this build

**The system must be fully demoable and testable end to end using only sandbox/mock
credentials.** Swapping sandbox keys for production keys (M-Pesa, Airtel Money, bank APIs,
SMS, IdentityServer signing keys) must be a configuration change only — never a code change.
See `docs/integrations/payment-providers.md` and `docs/runbooks/production-cutover.md`.

## Quick start (everything sandbox, no production credentials)

```bash
docker compose up -d postgres                       # Postgres 16 on localhost:5432 (sacco/sacco)
dotnet run --project backend/seed/Sacco.Seed        # create db, migrate all modules, seed the Demo SACCO
dotnet run --project backend/src/Sacco.Api          # API + OIDC server on http://localhost:5000 (Scalar UI at /scalar)
cd frontend/portal && npm install && npm run dev    # staff portal on http://localhost:3000
cd frontend/public-site && npm install && npm run dev   # public site on http://localhost:3001
```

Demo logins (tenant `demo`, password `Demo2026!pass`): `admin`, `teller`, `loanofficer`, `committee1`, `committee2`,
`manager`, `accountant`, `compliance` — see `backend/seed/README.md` for what each can demo. Tests:

```bash
dotnet test --project backend/tests/Sacco.UnitTests
dotnet test --project backend/tests/Sacco.IntegrationTests   # real Postgres via Testcontainers (Docker required)
```

## Top-level layout

| Path | Purpose |
|---|---|
| `.github/` | CI/CD workflows, PR template, code ownership |
| `.claude/` | Agent instructions, specialized agent personas, project skills |
| `backend/` | .NET 10 modular monolith (API, domain modules, Wolverine sagas, EF Core, seed data) |
| `frontend/portal/` | Authenticated staff/member Next.js app with BFF layer |
| `frontend/public-site/` | Public, tenant-branded marketing + membership-application site |
| `mobile/` | Member-facing or field-agent mobile app (scope TBD — see `mobile/CLAUDE.md`) |
| `infrastructure/` | IaC, per-environment config |
| `docs/` | ADRs, compliance mapping, integration specs, roadmap, runbooks |
| `shared-contracts/` | Generated OpenAPI spec / TS client — single source of truth for API shape |

## Tech stack (see ADRs for rationale)

- Backend: C# / .NET 10, PostgreSQL, EF Core, Wolverine (messaging + sagas, MIT-licensed)
- Auth: Open.IdentityServer (Apache 2.0 core), ASP.NET Core policy-based authorization
- Frontend: Next.js (App Router), Tailwind, shadcn/ui
- Bot protection: Cloudflare Turnstile on public forms
- Infra: containerized, environment-parameterized (dev / staging / production)
