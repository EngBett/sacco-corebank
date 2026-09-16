# CLAUDE.md — backend

Read `../.claude/CLAUDE.md` first — this file only adds backend-specific detail.

## Solution shape

One .NET 10 solution, `Sacco.sln`, structured as a modular monolith:

```
backend/src/
  Sacco.Api/                 ASP.NET Core host — minimal-API endpoints, composition root
  Sacco.Modules.Platform/    Tenants + branding, append-only audit log, tenant resolution middleware
  Sacco.Modules.Identity/    Staff users, roles → permission bundles, Open.IdentityServer host (Phase 2)
  Sacco.Modules.Members/     KYC, member profiles, exit/withdrawal workflow
  Sacco.Modules.Ledger/      GL, FOSA/BOSA-tagged accounts, double-entry posting engine
  Sacco.Modules.Savings/     Savings products, shares, dividends
  Sacco.Modules.Lending/     Loan products, guarantors, approval workflow, provisioning
  Sacco.Modules.Reporting/   SASRA returns, FOSA/BOSA and consolidated statements
  Sacco.Modules.Notifications/ Per-user in-app notifications, SignalR hub, hub tickets (ADR 0009)
  Sacco.Sagas/                Wolverine sagas — payment provider orchestration, batch check-off
  Sacco.Shared/                Cross-module contracts, common auth policies, permission constants
  Sacco.Migrations/            EF Core migrations (one folder + history table per module schema)
backend/tests/
  Sacco.UnitTests/
  Sacco.IntegrationTests/      Real Postgres (Testcontainers), sandbox payment providers
backend/seed/Sacco.Seed/       Seed data tool — see docs/testing/seed-data-strategy.md
```

Each module exposes `Add<Module>Module(...)` for DI and an `IModuleEndpoints` implementation the
host maps automatically. Each module owns one Postgres schema (`platform`, `ledger`, ...) and
its own `DbContext` derived from `Sacco.Shared.Persistence.ModuleDbContext`, which applies the
tenant query filter and snake_case naming.

## Ledger invariants (Phase 1)

- A journal must balance overall **and within each FOSA/BOSA segment**. Cross-segment flows go
  through the inter-segment clearing pair (BOSA `1800 Due from FOSA` / FOSA `2800 Due to BOSA`),
  so the FOSA and BOSA trial balances each balance on their own and net to zero on consolidation.
- Balances change only in `PostingEngine.ApplyBalancesAsync` via atomic conditional `UPDATE`s
  inside the posting transaction, touching accounts in ascending id order (no deadlocks, no
  distributed lock — ADR 0004). Running balances are reconciled against journal lines by
  `GET /api/ledger/reconciliation`.
- Manual journals and reversals are maker-checker: `PendingApproval` → `Posted` only when a
  *different* user approves. System postings from other modules (`ILedgerService.PostAsync`) post
  directly because their own workflow already carried the approval.
- Other modules reference sub-ledger accounts by account number and member accounts by an opaque
  `MemberId` — the ledger never joins to member tables.

## Identity & tenancy (Phase 2)

- Open.IdentityServer runs in-process in `Sacco.Api` (`/connect/*`, `/.well-known/*`, login UI at
  `/account/login`). Clients are configuration (`IdentityServer:Clients`), never code. The API validates
  its own tokens locally via the signing-key store, so no discovery round-trip.
- Tokens carry `sub`, `name`, `role` (names only) and `tenant`. Permissions are never in the token:
  `PermissionResolver` computes them per request from role bundles (cached 60 s, invalidated on change).
- Tenant resolution order: custom domain / subdomain → `X-Tenant` header → token `tenant` claim →
  `Tenancy:DefaultTenantSlug` (non-production only). A header that names an unknown tenant is a 400;
  a token for a different tenant than the request is a 403.
- Password grant is enabled only for the first-party `sacco-cli` client (demo/tests). The portal uses
  authorization code + PKCE via the BFF; the mobile client is a public PKCE client.
- Each module's `DbContext` must be used with a resolved tenant; `ModuleDbContext.SaveChanges` refuses
  to write tenant rows otherwise. Auth flows on tenant-agnostic routes bind the tenant from the credentials.
- EF gotcha: a child entity with a pre-set key added to a *tracked* aggregate is treated as Modified,
  not Added. Domain methods that create children (`Member.AddDocument`, `Role.SetPermissions`,
  `StaffUser.SetRoles`) return them so the service can `db.Add` them explicitly.

## Savings (Phase 3)

- Balances live in the Ledger; `savings.accounts` carries product/term state only. Account number =
  `{MemberNumber}-{ProductSuffix}` (`-FO`, `-SV`, `-SH`, `-FD1`…). Only KYC-verified members can open accounts.
- Every posting goes through `PostingBuilder`, which inserts the inter-segment clearing pair when a
  FOSA settlement account (teller cash, M-Pesa, bank) funds or pays a BOSA account.
- Withdrawals: teller cash within the product's `TellerWithdrawalLimit` on an on-demand product is paid
  in one step; everything else is a `WithdrawalRequest` (maker-checker, ledger hold for amount + fee,
  notice period enforced at payout). Mobile-money payouts are executed by the Payments module (Phase 5).
- Deposits are idempotent on the caller's receipt/transaction reference (`DEP:<reference>`).
- `SavingsSettings` maps the GL codes the module posts to; defaults match the demo chart of accounts.

## Lending (Phase 4)

- Eligibility reads BOSA deposits/shares via `ISavingsService.GetMemberSummaryAsync` and member standing via
  `IMemberDirectory` — never the other modules' tables. Max loan = deposits × product multiplier.
- Approval is an N-of-M workflow on `Loan.Approvals`: `product.ApprovalsRequiredFor(amount)` (1, or the
  committee count above the product threshold). No approver may be the originator or the appraiser; the
  disburser may not be the originator. Guarantee coverage (accepted guarantees + own deposits ≥ amount) is
  checked at approval.
- Guarantees and the borrower's own pledged deposits are ledger *holds* on BOSA deposit accounts, so
  exposure is enforced by available-balance arithmetic (`LendingSettings.MaxGuaranteeToDepositsRatio`,
  `MaxActiveGuaranteesPerMember`). Holds are released on rejection or closure.
- Disbursement opens the loan sub-ledger account (`LN-000123`, kind Loan) and credits the member's FOSA
  account net of the processing fee through the segment bridge. Repayments allocate interest → principal per
  instalment; interest not yet accrued is credited straight to income, accrued interest clears the receivable.
- `POST /api/loans/accrue-interest` recognises interest on due instalments (idempotent per instalment).
- NPL aging and provisioning rates are `ProvisioningConfig` rows (seeded with the SASRA schedule; confirm
  the current circular before Phase 6). A `ProvisioningRun` is computed by one user and posted by another.
- Credit scoring (ADR 0010): `CreditScoringService` scores every application at capture and again at appraisal (and on
  demand via `POST /api/loans/{id}/score`, permission `loans.appraise`). `CreditScoringEngine` is a pure function of the
  tenant `Scorecard` (`/api/loans/scoring/scorecard`, permission `loans.scoring.manage`) and `ScoringInputs`; every run is
  stored in `loan_credit_scores` with its factor breakdown. `ICreditBureau` is sandbox by default (ID ending 0 = listed,
  9 = unavailable); `Lending:CreditBureau:Mode=Live` needs a real provider. The recommendation never changes loan status.
- Write-off and restructuring are `LoanAdjustment` requests (maker `loans.restructure`, checker `loans.approve`): approval of a
  write-off charges the provision GL and reverses uncollected accrued interest, closes the ledger account and releases
  guarantees; approval of a restructure rebuilds the schedule over the outstanding principal (due interest carried into the
  first instalment). Instalments record `PaidAt`, so the repayment-history factor sees late payments on closed loans.
- `LendingMaintenanceService` (API host only, `Lending:Maintenance`) accrues interest daily and purges bureau narratives past
  `Lending:CreditBureau:RetentionDays`. Applications carry the member's bureau consent (`Lending:CreditBureau:ConsentText`).
- Member exit: `ILendingService.SettleOnExitAsync` pays every active loan off from BOSA deposits (`RepaymentChannel.DepositsOffset`,
  future interest waived) and refuses while the member guarantees others; `ISavingsService.CloseAccountsOnExitAsync` pays out
  and closes the rest. Orchestrated by `MemberService.ApproveExitAsync` (maker `members.exit`, checker `members.exit.approve`).

## Payments (Phase 5)

- `Sacco.Modules.Payments` owns `IPaymentProvider` implementations, `PaymentTransaction` rows and the two Wolverine sagas.
  Wolverine is configured in `PaymentsModule.ConfigureWolverine` (durable Postgres store + lightweight saga tables in
  schema `wolverine`, `Solo` durability mode outside production). Every saga message carries the tenant and the handler
  binds `TenantContext` first, because sagas run outside an HTTP request.
- Provider callbacks hit `/api/payments/webhooks/{tenant}/{provider}`; `TenantResolutionMiddleware` takes the tenant from
  that path (`Tenancy:PathTenantPrefixes`). Processing is inline (`IMessageBus.InvokeAsync`) so the provider's HTTP response
  reflects the outcome; replays are idempotent through `processed_provider_transactions`.
- The seed tool registers a stub `IMessageBus`; it writes fixtures through `PaymentFinalizer` directly and never runs sagas.
- Live providers: `DarajaMpesaProvider` (M-Pesa), `AirtelMoneyProvider` (Airtel Money Open API, from `reference/Brij.AirtelMoney`),
  `EquityJengaProvider` (`Bank:EQUITY`) and `NcbaProvider` (`Bank:NCBA`), all unit-tested through a fake HTTP handler and driven
  end to end against `mocked-providers/` (`e2e/run-local.sh`). Providers that cannot set callback headers validate a shared
  secret from the callback URL: the webhook endpoint exposes query parameters as `query:<name>` entries in `WebhookPayload.Headers`.
  A provider that settles synchronously returns `Completed = true` with the receipt and the saga finalises without a callback.
  Banks live under `Payments:Banks:{CODE}`; `Payments:DefaultBankProvider` picks the one for bank-transfer withdrawals.

## Reporting (Phase 6)

- `Sacco.Modules.Reporting` reads only the shared contracts: `ILedgerService.GetTrialBalanceAsync / GetGlActivityAsync / ReconcileAsync`
  and `ILendingService.GetPortfolioQualityAsync`. `ReportingSettings` maps GL codes onto return lines and holds the prudential
  thresholds (basis points) with their source; SDGF is null until confirmed.
- A `StatutoryReturn` is an immutable JSON package (FOSA/BOSA/consolidated position, income statements, capital adequacy,
  liquidity, portfolio quality, large exposures) plus eight reconciliation checks. It cannot be submitted unless every check
  passed, and the submitter must differ from the generator. `OpenItems` in every package lists what still needs SASRA confirmation.
- Historical dates: aging/provisioning use each loan's ledger statement closing balance as of the date, so a return for a past
  period end reconciles to that day's trial balance.
- Nightly PDF digest (ADR 0013): `DailyDigestScheduler` (API host only, `Reporting:DailyDigest`, default midnight UTC) mirrors
  `LendingMaintenanceService`'s per-tenant-DI-scope shape. `DailyDigestHtml` + `HtmlToPdfRenderer` (headless Chromium via
  PuppeteerSharp — production points it at the system Chromium in `backend/Dockerfile` via
  `Reporting:DailyDigest:ChromiumExecutablePath`) turn the same figures `StatutoryReportService` already computes into a
  branded PDF, sent through the Shared `IEmailSender` (attachment-capable, `Sacco.Shared.Notifications` — Reporting depends on
  it exactly like it depends on `INotifier`, never on the Notifications module directly). Recipients are plain
  `ReportRecipient` rows (`Permissions.Reporting.RecipientsManage`), not staff users — a board member needs no portal login to
  receive the email. `/api/reporting/daily-digest/preview.pdf` and `/run` let an admin see or send today's digest on demand.

## Member self-service (ADR 0008)

- `identity.member_logins`: phone + PIN, provisioned from the Members module through `IMemberLoginProvisioner`. The password
  validator and the interactive login try staff credentials first, then member phone + PIN; member tokens carry `member_id`
  and `PermissionResolver` grants the fixed `Permissions.Self` set. `ICurrentUser.RequireMemberId()` guards `/api/self/*`,
  which live in the module that owns the data (Members, Savings, Ledger, Lending, Payments) and scope every query to the token's member.

## Tenancy hardening (ADR 0011)

- Row-level security is **forced** after migrations (`RowLevelSecurity.ForceTenantIsolationAsync`, called by the API host and
  the seed tool), and `TenantConnectionInterceptor` clears `app.tenant_id` when no tenant is resolved. Background work must bind
  a tenant per scope (`ITenantEnumerator` + `TenantContext.Set`) or it reads nothing.

## Notifications (real-time)

- `Sacco.Modules.Notifications` implements `INotifier` (Sacco.Shared): domain services call it right after the audit
  write at every maker-checker hand-off and outcome (journals, withdrawals, loans, provisioning runs, KYC, membership
  applications, payments, statutory returns). Audience is explicit users or `HoldersOf(permission)`; the actor is never
  told about their own action. Rows are persisted per recipient, then pushed over SignalR (`/hubs/notifications`) to the
  `tenant:{id}:user:{id}` group.
- Browsers never hold the API token, so the hub authenticates with a **hub ticket**: `POST /api/notifications/hub-ticket`
  mints a 15-minute JWT (same signing key, audience `sacco-hub`) accepted only by the `HubTicket` scheme on `/hubs/*`.
  See ADR 0009. `Cors:AllowedOrigins` (portal origins) applies to the hub only.
- The seed tool registers the module without the SignalR pusher; notifications appear as a side effect of the seeders
  driving the real workflows, plus a welcome note per user (`NotificationsSeeder`).
- Out-of-band delivery: `OutboundDispatcher` writes `notifications.outbound_messages` (email for every notification, SMS for the
  kinds in `Notifications:Delivery:SmsKindPrefixes`) and sends through `ISmsSender`/`IEmailSender` — sandbox (DB row only, no
  network call) by default, a real gateway when `Notifications:Sms:Mode` / `Notifications:Email:Mode` are `Live`. A failed send
  is logged and left `Failed` on the row (retryable from `/api/notifications/outbox`); it never throws back into the workflow
  that raised the notification. `Notifications:Redis` enables the SignalR backplane for multiple replicas.
- Both channels are provider-agnostic (ADR 0012). `SmtpEmailSender` speaks plain SMTP, so every email provider — Mailpit
  locally, SES/SendGrid/a SACCO's own mail server in production — is a `Notifications:Email:Smtp:Host/Port` config change,
  no new code. SMS additionally has `Notifications:Sms:Provider` (`AfricasTalking` default, plus `Twilio`, `WhatsApp`,
  `Safaricom`, `Airtel`, `Mock`) selecting the concrete `ISmsSender` in `NotificationsModule.RegisterLiveSmsSender`; every
  sender lives in `Channels/SmsProviders.cs` and every caller (`OutboundDispatcher`) only ever sees the interface. Local dev
  defaults (`appsettings.Development.json`, `docker-compose.yml`) route through Mailpit (SMTP catcher, UI at
  `:8025`) and the local mock SMS gateway (`mocked-providers/mocked-sms-server`, UI at `:5108`) — both start by default
  alongside `postgres`, need no credentials, and are what `docker compose up -d postgres mailpit mocked-sms` gives you.
  `Twilio`/`WhatsApp` are written to the stable, well-documented vendor APIs; `Safaricom`/`Airtel` SMS are starter scaffolds
  (no public sandbox exists for either) — confirm the endpoint and payload against the SACCO's actual contract before go-live,
  the same caveat every live payment provider in this codebase already carries.

## Running locally

```bash
docker compose up -d postgres mailpit mocked-sms     # from repo root — mailpit/mocked-sms are the local mail/SMS channels
dotnet run --project backend/seed/Sacco.Seed        # migrate + seed Demo SACCO (idempotent; add -- --reset to rebuild)
dotnet run --project backend/src/Sacco.Api          # http://localhost:5000/scalar for the API reference
dotnet test --project backend/tests/Sacco.UnitTests
dotnet test --project backend/tests/Sacco.IntegrationTests   # needs Docker (Testcontainers) or SACCO_TEST_CONNECTION
```

Add a migration after changing a module's model:

```bash
dotnet ef migrations add <Name> --project backend/src/Sacco.Migrations --startup-project backend/src/Sacco.Migrations --context LedgerDbContext --output-dir Ledger
```

then call `migrationBuilder.EnableTenantIsolation(schema, table)` in `Up()` for every new
tenant-scoped table (row-level security, defence in depth behind the EF query filter).

## Module boundary rule

A module may only depend on another module's public interface (exposed via `Sacco.Shared`
contracts), never its internal types or EF `DbContext`. If `Sacco.Modules.Lending` needs a
member's BOSA savings balance, it calls an interface `Sacco.Modules.Ledger` implements — it
does not reference `Sacco.Modules.Ledger`'s DbSet directly. This is what makes a future
extraction into a separate service (if ever needed) a boundary-preserving move rather than a
rewrite.

## Database

Single Postgres database. Multi-tenancy via `tenant_id` + row-level security, unless a
specific SACCO's contract requires schema-per-tenant isolation (document that exception in an
ADR if it happens). Every table that can be scoped to FOSA/BOSA has an explicit column for it
— never infer it from account type alone.

## Messaging

Wolverine (MIT-licensed) — not MassTransit. Use it only where there's a genuine async
boundary: payment provider callbacks, batch payroll check-off processing, notification
dispatch. Core ledger/loan/member transactions stay as plain, synchronous Postgres
transactions.

## Auth

Open.IdentityServer issues tokens (OIDC). The API validates them and resolves the current
user's permissions server-side (cached, not baked permanently into the JWT) for immediate
revocation. Policy-based authorization (`[Authorize(Policy = "loans.approve")]`) throughout —
see the `sacco-domain-conventions` skill.

## Every PR

Must include or update: EF Core migration (if schema changed), seed data (see
`backend/seed/README.md`), unit tests, and — if it crosses a module boundary or touches
money movement — an integration test.
