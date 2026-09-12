# Production cutover runbook

Going live for a SACCO must be a configuration exercise, not a development exercise. If any
step here requires a code change, that's a bug in how the corresponding module was built —
fix the module, don't special-case the cutover.

## Pre-cutover checklist

- [ ] Tenant record created for the real SACCO (branding, name, domain)
- [ ] Real M-Pesa (Daraja) production credentials obtained and configured — provider
      selection switches from sandbox to production via configuration only
- [ ] Real Airtel Money production credentials obtained and configured
- [ ] Real bank API/settlement credentials obtained and configured (per ADR/contract for that
      SACCO's settlement bank)
- [ ] Open.IdentityServer production signing keys generated and stored in the secrets manager
      (never reused from a lower environment)
- [ ] SASRA reporting outputs validated by the compliance reviewer against real chart of
      accounts and real opening balances for this SACCO
- [ ] Data migration plan for the SACCO's existing member/loan/savings data (if migrating
      from a legacy system) reviewed and tested against a staging copy first
- [ ] Backup and point-in-time recovery confirmed working in production, not just configured
- [ ] Maker-checker roles assigned to real staff accounts, not left on seeded demo accounts
- [ ] Demo/seed data confirmed absent from the production database

## Rollback plan

Document, before go-live, what "roll back" means for this SACCO specifically — at minimum,
confirm database backups are restorable and payment provider webhooks can be safely paused
without losing in-flight transaction state.
