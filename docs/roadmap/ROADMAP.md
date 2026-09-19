# Roadmap

Work phase by phase. Each phase has an explicit, demoable, testable exit criterion — "done"
means a reviewer can pull the branch and walk through the criterion using seed data and
sandbox credentials only.

> Status legend: ✅ done (exit criterion demonstrated by seeded data + integration tests) · 🚧 in progress · ⏳ not started.
> Test evidence: `dotnet test --project backend/tests/Sacco.IntegrationTests` (53 tests) and `.../Sacco.UnitTests` (60 tests).

## Phase 0 — Repository scaffold ✅ done
- Folder structure, CLAUDE.md files, agent/skill definitions, ADRs, CI skeletons, roadmap.
- **Exit criterion**: repo exists, documented, ready to hand to an implementation agent.

## Phase 1 — Core ledger & GL engine ✅ done (2026-09-11)
- Postgres schema for accounts, FOSA/BOSA-tagged GL, double-entry posting engine, row-locking
  debit/credit operations (ADR 0004).
- Seed: chart of accounts, a handful of posted transactions that produce a balancing trial
  balance.
- **Exit criterion**: can post a transaction via API, see it reflected in FOSA, BOSA, and
  consolidated trial balance views; balances are correct under concurrent posting (covered by
  an integration test that fires concurrent debits at the same account).

## Phase 2 — Members, KYC, and auth ✅ done (2026-09-11)
- Member domain, KYC states, Open.IdentityServer integration, permission-based authorization
  (roles/permissions/policies), maker-checker primitives in `Sacco.Shared`.
- Seed: demo users per role, demo members across KYC states.
- **Exit criterion**: can log in as each seeded role and see role-appropriate access; a
  permission-gated action correctly denies an unauthorized role.

## Phase 3 — Savings & shares (BOSA), FOSA transactional accounts ✅ done (2026-09-11)
- BOSA savings/fixed-deposit/share products, FOSA on-demand accounts, notice-period
  withdrawal rules, dividend computation skeleton.
- Seed: products, member savings histories sufficient to demonstrate loan-eligibility
  calculations in Phase 4.
- **Exit criterion**: can open a savings account, deposit, request a withdrawal (respecting
  notice-period rules), see FOSA vs BOSA correctly reflected in the ledger.

## Phase 4 — Lending ✅ done (2026-09-11); write-off, restructuring, member-exit settlement, credit scoring (ADR 0010), bureau consent/retention and the daily accrual scheduler added 2026-09-14
- Loan products, guarantor exposure tracking, approval workflow (maker-checker, N-of-M
  committee for larger amounts), disbursement into FOSA, provisioning/NPL aging (configurable).
- Seed: loans across every aging bucket, a guarantor near their exposure cap, a pending
  approval.
- **Exit criterion**: can originate a loan, have it guaranteed, route it through approval,
  disburse it, and see correct GL postings and provisioning classification.

## Phase 5 — Payments integration ✅ done (2026-09-11) — live M-Pesa (Daraja) provider written but unverified against a real account; Airtel Money and bank live providers still to be written
- `IPaymentProvider` contract, sandbox implementations for M-Pesa, Airtel Money, and bank
  transfer; Wolverine sagas for collection/disbursement; idempotent webhook handling.
- Seed: fixtures per provider (success, failure, duplicate webhook, disbursement).
- **Exit criterion**: can trigger a sandbox M-Pesa/Airtel Money collection end-to-end (STK
  push simulated → webhook → ledger posting), replay the same webhook and confirm no
  duplicate posting occurs.

## Phase 6 — Regulatory reporting ✅ done (2026-09-11) — SASRA open items encoded as configuration and surfaced in every package
- SASRA statutory returns (FOSA/BOSA split + consolidated), capital adequacy and liquidity
  ratio computation, large-exposure reporting.
- Resolve the open items listed in `docs/compliance/sasra-mapping.md` before building this
  phase — confirm current provisioning percentages, aging bucket definitions, and submission
  format against SASRA's current published guidance.
- **Exit criterion**: can generate a full statutory return package from seeded data and have
  it reconcile against the underlying ledger to the cent.

## Phase 7 — Frontend: portal, public site, branding ✅ done (2026-09-12) — portal (31 routes, OIDC BFF) and public site (membership application via Turnstile) build clean and were smoke-tested end to end against the seeded API
- `frontend/portal` (BFF, RBAC-aware UI, dashboards per `DESIGN_GUIDELINES.md`),
  `frontend/public-site` (tenant-branded, Turnstile-protected membership application),
  tenant branding data model and Middleware-based resolution.
- Resolve `mobile/CLAUDE.md`'s open scope question before or during this phase if mobile is
  in scope for the initial release.
- Added 2026-09-14: shadcn `dashboard-01` shell, real-time in-app notifications (SignalR + hub tickets, ADR 0009) with
  a bell/inbox, and a light/dark/system theme toggle.
- Added 2026-09-15: provider-agnostic outbound SMS/email (ADR 0012) — `Notifications:Sms:Provider` switches between
  Africa's Talking, Twilio, WhatsApp, Safaricom, Airtel or a local mock with no code change; local dev now routes
  through Mailpit and `mocked-providers/mocked-sms-server` by default instead of a silent sandbox stub.
- Added 2026-09-16: nightly branded PDF digest (ADR 0013) — capital adequacy, liquidity and portfolio quality rendered
  to PDF via headless Chromium (PuppeteerSharp) at midnight and emailed to admin-configured recipients; `IEmailSender`
  is now a Shared contract with attachment support, and Admin → Daily PDF digest previews/sends it on demand.
- Added 2026-09-16: `mobile/` — the member self-service Flutter app (ADR 0008: login,
  accounts/balances, statements, deposits via M-Pesa/Airtel Money/Equity, withdrawals via
  M-Pesa/Airtel Money/bank Pesalink, dividends by financial year). Needed two new endpoints,
  `POST/GET /api/self/withdrawals` and `GET /api/self/dividends`, both maker-checker safe.
- **Exit criterion**: Icodeio SACCO's portal and public site both render correctly themed, every
  seeded role can log into the portal and see role-appropriate screens, a Turnstile-protected
  membership application submits successfully to a pending-review queue.

## Phase 8 — Compliance hardening ✅ signed off (2026-09-12; F2/F3/F4/F5 closed 2026-09-14) — see `docs/compliance/phase8-signoff.md`; F1 (live providers) and F6 (SASRA figures) close during cutover via `docs/compliance/sasra-confirmation-register.md`
- Audit logging completeness pass (every approval/denial/GL adjustment), maker-checker
  coverage audit, load testing on concurrent debit scenarios, security review of public
  surfaces.
- **Exit criterion**: `compliance-reviewer` agent (or a human standing in for it) signs off
  against `docs/compliance/sasra-mapping.md` with no open flags.

## Phase 9 — Production cutover 🚧 infrastructure ready (Dockerfiles, compose stack, AWS Terraform for dev/staging/production); the cutover itself awaits a real SACCO
- See `docs/runbooks/production-cutover.md`.
- **Exit criterion**: a real SACCO's production credentials are configured (not coded), and
  the system behaves identically to the sandbox-backed demo, minus using real money.
