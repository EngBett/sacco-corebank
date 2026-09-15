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

## Real-time notifications (ADR 0009)

- Set `Cors:AllowedOrigins` to the portal origin(s) (wildcard tenant subdomains are allowed, e.g. `https://*.portal.example`)
  and the portal's `API_BROWSER_URL` to the API origin the browser reaches. The Terraform app module does both.
- SignalR keeps connections in process. Before running more than one API task, add a backplane
  (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`, MIT) so a notification raised on one instance reaches sessions on another,
  or pin the hub to a single instance behind the load balancer with sticky sessions.

## Added 2026-09-14

- **Load test before go-live:** run `backend/tests/load/concurrent-debits.js` against staging (see `backend/tests/load/README.md`); record the p95 and confirm no overdraw.
- **Notification channels (ADR 0012):** set `Notifications:Email:Mode=Live` with real SMTP settings (`Notifications:Email:Smtp:*`) —
  any SMTP provider works, no code change. Set `Notifications:Sms:Mode=Live` and `Notifications:Sms:Provider` to the SACCO's
  chosen gateway (`AfricasTalking`, `Twilio`, `WhatsApp`, `Safaricom`, or `Airtel`) with that provider's own settings block
  populated from the secrets manager; never leave `Provider=Mock` outside `Development`. `Safaricom`/`Airtel` SMS are starter
  scaffolds pending the SACCO's actual contract — rehearse against the vendor's sandbox first, the same way each payment
  provider was rehearsed against `mocked-providers/` before go-live. Verify one message on each channel from `/admin/outbox`
  after deployment.
- **Redis backplane:** set the Terraform variable `enable_redis_backplane = true` (wires `Notifications:Redis`) before raising `desired_count` above 1.
- **Credit bureau:** `Lending:CreditBureau:Mode=Live` needs a provider implementation for the SACCO's bureau; until then leave Sandbox off and accept that every lookup reports "unavailable" (scores refer rather than approve). Confirm the consent wording (`Lending:CreditBureau:ConsentText`) and retention (`RetentionDays`) with the bureau agreement.
- **Daily maintenance:** `Lending:Maintenance:RunAtUtc` (default 02:00) drives interest accrual and bureau purges; confirm one run in the logs after the first night.
- **SASRA figures:** work through `docs/compliance/sasra-confirmation-register.md`; the first statutory package must show an empty `OpenItems`.
- **Row-level security** is forced automatically after migrations; no separate database role is required for isolation (ADR 0011).
- **Airtel Money live:** set `Payments:AirtelMoney:Mode=Live`, `BaseUrl=https://openapi.airtel.africa/`, `ClientId`/`ClientSecret`,
  `Country=KE`, `Currency=KES`, and for payouts `DisbursementPin` + `DisbursementPublicKey` (from the Airtel merchant portal), all
  from the secrets manager. Register the callback URL `https://<api>/api/payments/webhooks/<tenant>/airtelmoney?token=<CallbackToken>`
  with Airtel and add Airtel's callback source ranges to `Payments:WebhookAllowedCidrs`. Run one USSD push and one small
  disbursement on the UAT sandbox first (`openapiuat.airtel.africa`).
- **Bank providers:** configure `Payments:Banks:EQUITY:*` (Jenga API key, merchant code, consumer secret, short code, PIN,
  callback token) and/or `Payments:Banks:NCBA:*` (API key, API user, default bank/branch codes) from the secrets manager, set
  `Mode=Live` and choose `Payments:DefaultBankProvider` for bank-transfer withdrawals. Rehearse with
  `mocked-providers/e2e/run-local.sh` against the provider's UAT host first.
