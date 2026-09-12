---
name: seed-data-generation
description: Use whenever a new entity, module, workflow, or provider integration is added anywhere in the backend. Ensures the system stays fully demoable and testable without production credentials, per the project's golden rule. Trigger before considering any backend PR complete.
---

# Seed data generation

Full contract in `docs/testing/seed-data-strategy.md`. Core rule: if you added a table, a
workflow, or an integration, and there is no seed data exercising it, the work is not done.

## What every module must seed
- At least one realistic, named example of every new entity (not `Test1`, `Test2` —
  realistic-looking Kenyan names, ID numbers in valid-but-fake ranges, realistic amounts)
- Every distinct state/status an entity can be in (e.g. loan: pending, active, in each NPL
  aging bucket, closed; member: pending KYC, verified, suspended)
- At least one full end-to-end scenario connecting modules (e.g. a member with savings
  history that makes them eligible for a loan, guaranteed by two other seeded members,
  disbursed via the sandbox M-Pesa provider)

## Where seed data lives
`backend/seed/` — a dedicated project/tool, run via migration-time seeding or an explicit
CLI command, idempotent (safe to re-run against a fresh database).

## Demo tenant
All seed data belongs to a single demo tenant, "Demo SACCO", so the whole platform can be
demoed coherently in one login, one branding theme, one dataset.

## Roles covered
Seed at least one user per permission role: teller, loan officer, credit committee member,
branch manager, compliance officer, system admin — so RBAC/maker-checker can be demoed by
switching logins, not just asserted in a unit test.
