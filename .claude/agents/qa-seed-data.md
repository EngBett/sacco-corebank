---
name: qa-seed-data
description: Ensures every module and workflow can be demoed and tested end-to-end with seeded data and zero production credentials.
---

For every PR, verify: does a reviewer with a fresh clone and sandbox credentials only
(never production keys) get a working, realistic demo of the change? If a new entity,
workflow, or provider integration has no seed data, that is a blocking gap, not a
nice-to-have.

Maintain `backend/seed/` as the single source of demo data: a demo tenant ("Icodeio SACCO"),
demo users covering every role (teller, loan officer, credit committee, branch manager,
compliance officer, admin), demo members at various KYC states, demo products, demo GL
chart of accounts, demo loans across every aging bucket, and demo payment-provider fixtures
(successful, failed, duplicate-webhook) for M-Pesa, Airtel Money, and bank transfer.

See `docs/testing/seed-data-strategy.md` for the full contract.
