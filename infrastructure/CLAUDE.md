# CLAUDE.md — infrastructure

IaC for every environment (`environments/dev`, `environments/staging`,
`environments/production`), parameterized rather than duplicated where possible.

## Non-negotiables

- Every secret (payment provider credentials, IdentityServer signing keys, DB credentials)
  comes from a secrets manager, never committed, never hardcoded in an environment file
  checked into this repo.
- Dev and staging default to **sandbox** payment provider credentials. Production is the only
  environment where real M-Pesa/Airtel Money/bank credentials are configured — and per the
  project's golden rule, that switch must require zero code changes.
- Database backups and point-in-time recovery are configured before any environment holds
  real member data — this is a regulatory expectation (SASRA), not just good practice.
- Multi-tenant isolation approach (shared DB with row-level security vs. schema-per-tenant)
  must match what's documented in `backend/CLAUDE.md` / the relevant ADR — don't let infra
  and application-layer tenancy assumptions drift apart.
