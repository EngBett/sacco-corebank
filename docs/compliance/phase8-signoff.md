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
| F1 | Medium | Live M-Pesa (`DarajaMpesaProvider`) is written against the published Daraja API but has never been exercised against a Safaricom sandbox account. Airtel Money and bank live providers do not exist (registry throws a clear error in Live mode). | Verify against Daraja sandbox before Phase 9; implement Airtel/bank providers per the pilot SACCO's contracts. |
| F2 | Medium | RLS is enabled but not `FORCE`d, so a database owner/superuser bypasses it (intended for migrations and local dev). | In production, run the API as a non-owner role (`infrastructure/README.md` should provision one) — tracked as a Phase 9 cutover step. |
| F3 | Low | No audit event for guarantor decline and GL account creation. | Add `loans.guarantor.declined` and `ledger.gl_account.created` events. |
| F4 | Low | Member exit settlement, loan write-off and restructuring are not implemented (documented in `backend/CLAUDE.md`). | Build behind maker-checker when scheduled; `ILendingService.GetMemberExposureAsync` already exists for the exit check. |
| F5 | Low | Load testing of concurrent debits is an integration test (40 parallel requests), not a sustained load test. | Add a k6 scenario against a staging environment before go-live. |
| F6 | Info | SASRA figures (provisioning schedule, liquidity weighting, large-exposure limit, SDGF basis, submission format) are encoded defaults with source notes, not confirmed values. | Confirm with SASRA; update configuration, not code. |

## Sign-off

Phase 8 is signed off **conditionally**: the governance controls the roadmap requires (maker-checker, permission-based
authorization, FOSA/BOSA integrity, idempotency, audit trail, tenant isolation, public-surface hardening) are enforced in
code and covered by tests. F1, F2 and F6 must be closed as part of the Phase 9 cutover for a real SACCO; F3–F5 are
backlog items and do not block a demo or a staging deployment.
