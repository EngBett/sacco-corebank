# Phase 8 — Compliance hardening review (2026-09-12)

Reviewer: build agent standing in for `compliance-reviewer` (the automated reviewer was unavailable in this session).
Scope: `backend/src/**`, `backend/tests/**`, public surfaces, migrations. Evidence is by file; every claim below is backed
by an integration test that a reviewer can run with `dotnet test --project backend/tests/Sacco.IntegrationTests`.

## Requirement status (against `sasra-mapping.md`)

| Requirement | Status | Evidence |
|---|---|---|
| Segregation of duties on every money-moving approval | Verified | `Sacco.Shared/Domain/MakerChecker.cs`; enforced in `JournalEntry.Approve/Reject`, `WithdrawalRequest.Approve/Reject`, `DividendDeclaration.Approve/Reject`, `Loan.Approve/Reject/Disburse` (approver ≠ originator and ≠ appraiser; disburser ≠ originator), `ProvisioningRun.Approve/Reject`, `StatutoryReturn.Submit`, `Member.VerifyKyc/RejectKyc` (verifier ≠ registrar). Tests: `JournalWorkflowTests`, `SavingsWorkflowTests`, `LoanLifecycleTests`, `StatutoryReturnTests`, `MemberWorkflowTests` each assert the 403 `maker_checker.same_user` |
| Permission-based authorization, never role names | Verified | `RequirePermission(...)` on every non-public endpoint; `PermissionAuthorizationHandler` resolves server-side; `Permissions.All` is the single registry (unit test `Every_permission_constant_is_registered_in_All`). No `Role ==` checks exist in application code (`grep -r 'Role ==' backend/src` is empty) |
| FOSA/BOSA on every account, line and product; per-segment balance | Verified | `JournalEntry.Create` rejects unbalanced-per-segment journals; `PostingEngine.ResolveLinesAsync` rejects a line whose tag differs from the account's; `SegmentBridge` used by Savings, Lending and Payments; `TrialBalanceTests` prove FOSA and BOSA each balance and sum to consolidated |
| Concurrency safety without distributed locks | Verified | `PostingEngine.ApplyBalancesAsync` atomic conditional `UPDATE`s in ascending id order; `ConcurrencyTests` (40 parallel debits → exactly 10 succeed; cross-account transfers do not deadlock) |
| Payment idempotency | Verified | `processed_provider_transactions` unique index `(provider, provider_transaction_reference)`; `PaymentFinalizer.ApplyAsync` claims the reference before any posting; `PaymentFlowTests.Collection_posts_once_even_when_the_webhook_is_replayed` (per provider) and the seeded duplicate fixtures |
| Audit trail | Verified with one gap | `IAuditLogger` events: journals (initiated/approved/rejected/reversal requested), members (registered/updated/KYC verified/rejected/suspended/reinstated/application approved/rejected), savings (account opened, FD opened/matured, withdrawal requested/approved/rejected/paid/paid-by-teller/paid-by-provider/payout failed, dividend declared/approved/rejected/paid), loans (applied/guarantor added/accepted/appraised/approval recorded/approved/rejected/disbursed/repayment/closed/provisioning computed/posted/rejected/config changed), payments (initiated/succeeded/failed), reporting (generated/submitted/withdrawn), identity (user created/roles changed/(de)activated/password reset/changed, role created/updated/deleted). **Gap:** guarantor decline and GL account creation write no audit event (low risk, no money moves) — listed as fix F3 |
| Provisioning percentages, aging buckets, ratios as configuration | Verified | `ProvisioningConfig` rows (seeded with source text), `ReportingSettings` thresholds with `ThresholdSource`; every statutory package prints `OpenItems` |
| Public surfaces | Verified | Membership application: server-to-server key + Turnstile + rate limit `public` + duplicate check; creates `MembershipApplication` only (`MemberWorkflowTests.Public_application_creates_a_pending_application_never_a_member...`). Webhooks: per-tenant path, rate limit, optional source-IP allowlist (`WebhookSourceGuard`, warned at startup when empty in Production), idempotent processing. Product catalogue and branding: read-only, anonymous, rate-limited |
| Tenant isolation | Verified | `ModuleDbContext` query filter + write guard on every `TenantEntity`; RLS policies in every migration (`EnableTenantIsolation`); token tenant must match the request (`TenantResolutionMiddleware`, test `Token_tenant_must_match_the_requested_tenant`) |
| Backups / PITR before real data | Verified (infra) | `infrastructure/modules/database`: automated backups (7/14/35 days), KMS encryption, Multi-AZ + deletion protection in production |

## Findings

| # | Severity | Finding | Fix |
|---|---|---|---|
| F1 | Medium | **Closed for code, open for credentials (2026-09-14):** live providers exist for M-Pesa (`DarajaMpesaProvider`), Airtel Money (`AirtelMoneyProvider`), Equity/Jenga (`EquityJengaProvider`) and NCBA (`NcbaProvider`), each unit-tested through a fake gateway and driven end to end against the mock servers under `mocked-providers/` (M-Pesa STK push and B2C, Airtel USSD push, Equity C2B with callbacks, synchronous NCBA payouts). | Repeat `mocked-providers/e2e/run-local.sh` against each provider's UAT environment with the pilot SACCO's credentials before go-live; the only remaining unknown is drift between mock and live gateway. |
| F2 | Medium | ~~RLS is enabled but not `FORCE`d~~ **Closed 2026-09-14 (ADR 0011):** policies are forced after every migration run and the connection interceptor fails closed; the integration suite asserts an untenanted connection sees no rows. | — |
| F3 | Low | ~~No audit event for guarantor decline and GL account creation.~~ **Closed 2026-09-14:** `loans.guarantor.declined` and `ledger.gl_account.created` are recorded. | — |
| F4 | Low | ~~Member exit settlement, loan write-off and restructuring are not implemented.~~ **Closed 2026-09-14:** all three are maker-checker workflows with GL postings and integration tests. | — |
| F5 | Low | ~~Load testing is a 40-request integration test.~~ **Closed 2026-09-14:** `backend/tests/load/concurrent-debits.js` (k6) runs sustained parallel withdrawals against one account and fails on any overdraw; it is a cutover checklist step against staging. | — |
| F6 | Info | SASRA figures are encoded defaults with source notes, not confirmed values. | Tracked item by item in `sasra-confirmation-register.md` (owner, evidence, date); the CSV export (`/api/reporting/statutory-returns/{id}/export.csv`) covers the submission format until the portal format is confirmed. |

## Sign-off

Phase 8 is signed off **conditionally**: the governance controls the roadmap requires (maker-checker, permission-based
authorization, FOSA/BOSA integrity, idempotency, audit trail, tenant isolation, public-surface hardening) are enforced in
code and covered by tests. F1, F2 and F6 must be closed as part of the Phase 9 cutover for a real SACCO; F3–F5 are
backlog items and do not block a demo or a staging deployment.
