# Seed data strategy

## Why this exists

Golden rule of this build: the system must be demoable and testable end-to-end using only
sandbox/mock credentials — no production keys needed until actual go-live. Seed data is what
makes that true. It is not a testing nice-to-have; it's a first-class deliverable of every
phase in the roadmap.

## Contract

Every module PR that introduces a new entity, status, workflow, or integration must also
update `backend/seed/` so that:

1. A fresh database, seeded from scratch, demonstrates the new capability without any manual
   setup beyond running the seed tool.
2. The seed data is realistic — plausible Kenyan names, ID numbers in an obviously-fake but
   correctly-shaped range, realistic KES amounts — not `Test1`/`foo@bar.com` placeholders,
   since realistic data surfaces UI/formatting issues placeholder data hides.
3. Every distinct state an entity can be in is represented at least once (see
   `.claude/skills/seed-data-generation/SKILL.md` for the specific list per module).
4. Seeding is idempotent — safe to re-run against a database that already has seed data,
   without duplicating or erroring.

## Demo tenant

All seed data belongs to one tenant: **Icodeio SACCO**. This lets a single login/subdomain
demo the entire platform coherently — one branding theme, one consistent dataset, one
walkthrough script.

## Roles covered

At minimum, one seeded user per permission role: teller, loan officer, credit committee
member, branch manager, compliance officer, system admin — so RBAC and maker-checker can be
demonstrated by switching logins, not just asserted in a unit test.

## Payment provider fixtures

For M-Pesa, Airtel Money, and bank transfer, seed: a successful collection, a failed/timed-
out collection, a duplicate webhook delivery (proving idempotency holds), and a successful
disbursement. These fixtures should be replayable against the sandbox provider
implementations in an integration test, not just static database rows.
