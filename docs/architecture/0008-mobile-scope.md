# ADR 0008: Mobile scope — member self-service app first, field-agent app deferred

## Status
Accepted (2026-09-14) as the direction for the initial release: the member self-service **API** described below is
built, tested and seeded (`/api/self/*`, phone + PIN logins, the `mobile` PKCE client).

**Update (2026-09-16):** the product owner picked **Flutter** (not React Native or a responsive web app) for the
app itself. Build started: login, accounts/balances, deposits, withdrawals and dividends-by-year — see the
Implementation note below for what shipped and the channel-scope decisions it required.

**Update (2026-09-16, later the same day):** login was redesigned from Authorization Code + PKCE via an
in-app browser to a **native phone+PIN sign-in with SMS OTP on first device + biometric unlock after** —
see "Implementation note — native login redesign" below. The `mobile` OIDC client no longer supports the
`code` grant at all.

## Context
`mobile/CLAUDE.md` names two candidate products: a member self-service app (balances, mini-statements,
loan applications, mobile-money top-ups) and a field-agent/loan-officer app for low-connectivity KYC
and appraisal work. They imply different architectures: the first is a thin online client; the second
needs local-first storage with sync and conflict resolution.

Phases 1–6 have produced everything the first product needs: OIDC with a public PKCE client (`mobile`
in `IdentityServer:Clients`), permission-based authorization, the savings/loan/payment endpoints, and
sandbox M-Pesa/Airtel Money collections that a member can trigger against their own account.

## Decision
For the initial release, scope `mobile/` to the **member self-service app**:
- Same .NET API and OIDC server as the portal; the app is a public PKCE client (`sacco://auth/callback`).
- Members authenticate with a member login (to be added to the Identity module: member credentials
  bound to a `MemberId`, with a `members.self` permission set — balances, statements, own loan
  applications, own payments). No staff permissions are ever granted to member logins.
- Online-only; no local persistence of financial data beyond a short-lived cache.

Defer the field-agent app. When it is picked up it needs its own ADR covering offline capture,
sync/conflict resolution, and how captured KYC data enters the staff-review workflow (never directly
creating verified members — non-negotiable #9).

## Consequences
- The backend gains a member-facing identity concept and a narrow "self" permission set before mobile work starts.
- Mobile shares the generated client in `shared-contracts/` with both Next.js apps.
- Field-agent requirements (offline KYC, appraisal in the field) are explicitly out of the initial release.

## Implementation note (2026-09-14)
- Member logins live in the Identity module (`identity.member_logins`): phone number + 4–6 digit PIN, lockout after
  five failures, enabled per member by staff (`POST /api/members/{id}/self-service`, permission `members.self_service.manage`)
  and disabled automatically on exit. Tokens carry `member_id`; the permission resolver grants the fixed `self.*` set.
- Endpoints: `/api/self/profile`, `/api/self/accounts`, `/api/self/summary`, `/api/self/statements/{account}`,
  `/api/self/loans` (list + apply with bureau consent), `/api/self/payments/topup` (sandbox M-Pesa/Airtel push to the
  member's own phone) and `/api/self/payments`. Every one scopes to the member in the token; other members' records are
  "not found", never "forbidden".
- Seed: every verified demo member can sign in with their phone number and PIN `2468` (`backend/seed/README.md`).

## Implementation note (2026-09-16) — Flutter app and the withdrawal/dividend endpoints it needed

Two self-service gaps had to be closed before the app could do everything asked of it (withdraw, deposit, see
dividends by year):
- **`POST /api/self/withdrawals`** — a member requests a withdrawal from their own account exactly like the
  portal's teller-initiated flow (`SavingsService.RequestWithdrawalAsync`, same channels, same `WithdrawalRequest`
  entity), scoped so another member's account is "not found," never "forbidden." **It is still maker-checker**: a
  member's own request lands `PendingApproval` and a staff user (`self.withdrawals.request` never appears on any
  staff role) must approve it before anything is paid out — a mobile app is exactly the kind of surface account
  takeover targets, so this is not a channel to relax the maker-checker non-negotiable on. `GET /api/self/withdrawals`
  lists the member's own requests so the app can show status (Pending → Approved → Paid).
- **`GET /api/self/dividends`** — the member's own line from every year they have one, reusing `DividendLine`/
  `DividendDeclaration` with zero duplication; optional `?year=` for a single year. Status reflects whatever the
  declaration's real status is (Declared/Approved/Paid) — the app shows a pending dividend as pending, not paid
  early.

**Withdrawal/deposit channel scope, decided while wiring the app:**
- **Withdrawal channels**: M-Pesa, Airtel Money, and one generic "Bank account (Pesalink)" option — not "NCBA" and
  "Equity" as two separate member-facing choices. Both `EquityJengaProvider` and `NcbaProvider` settle over the
  same Pesalink rails to *any* Kenyan bank; which one actually executes the transfer is `Payments:DefaultBankProvider`,
  an operational setting for the SACCO, not something a member needs to choose per withdrawal. The member picks their
  own destination bank and account number; the app never asks "which gateway."
- **Deposit channels**: M-Pesa, Airtel Money, and Equity (`Bank:EQUITY` supports C2B collections). **NCBA does not
  appear as a deposit option** — `NcbaProvider` is payout-only (`docs/integrations/payment-providers.md`: "bank
  collections arrive as unsolicited credits"); it has no member-initiated collection endpoint to call. If NCBA
  later exposes one, deposits gain it the same way Equity has it today.
- Both flows reuse `PaymentService`'s existing collection/disbursement machinery (`InitiateCollectionAsync` for
  deposits via `/api/self/payments/topup`, the existing maker-checker withdrawal + disbursement flow for payouts) —
  no new payment-provider code, only the two self-service endpoints above.

## Implementation note (2026-09-16) — native login redesign: phone+PIN, SMS OTP once per device, biometric after

The browser-based Authorization Code + PKCE flow (via `flutter_appauth` and a Custom Tab) was replaced with a
**native** sign-in before the app shipped its first real user: the member never leaves the app, and the whole
Custom-Tab/redirect-chain fragility this session hit repeatedly (Brave blocking the app hand-off, Chrome's
external-protocol trust requirements rejecting synthetic taps) goes away because there is no browser hop at all.

**Shape of the flow:**
1. `POST /api/self/auth/otp/request` (new, `Sacco.Modules.Identity`) — checks phone + PIN via the existing
   `MemberLoginService.AuthenticateAsync` (same lockout-after-5-failures behaviour as before) and, unless this
   `device_id` has already completed OTP once for this member, sends a 6-digit SMS code and returns
   `{ otpRequired, expiresInSeconds }`. A wrong PIN never reaches the SMS-sending step. Requesting again for an
   already-pending code reuses it rather than spamming a fresh SMS per retry.
2. `POST /connect/token` (`grant_type=password`, `client_id=mobile`) — the same ROPC endpoint `sacco-cli` has used
   for tests since Phase 2, now also allowed for the public `mobile` client. Two new form fields: `device_id`
   (always) and `otp` (only when step 1 said `otpRequired: true`). `SaccoPasswordValidator` now:
   - **Never attempts staff authentication at all when `client_id=mobile`** — a public, no-secret client must not be
     a second surface for brute-forcing staff passwords, so the mobile path goes straight to member PIN auth.
   - On successful PIN auth, checks whether `device_id` is already a trusted `MemberTrustedDevice` row; if so, the
     grant succeeds immediately (no OTP). If not, it requires and verifies `otp` against the pending
     `MemberOtpChallenge`, and on success creates the trusted-device row.
   - Device trust and OTP challenges are new tenant-scoped tables (`identity.member_trusted_devices`,
     `identity.member_otp_challenges`), same RLS/migration shape as `member_logins`. An OTP challenge is single-use,
     5-minute lifetime, capped at 5 verification attempts (mirrors `MemberLogin`'s own lockout shape rather than
     inventing a new one).
3. The app stores the resulting refresh token behind the device's **biometric lock** (`flutter_secure_storage` +
   `local_auth`) instead of re-prompting for a PIN on every open — the standard pattern in Equity/KCB/M-Pesa's own
   apps. Losing/resetting the device, or the SACCO revoking a `MemberTrustedDevice` row, forces the next sign-in
   back through OTP.

**Why ROPC instead of hand-minting tokens after OTP verification:** the alternative — mint access/refresh tokens
directly via Open.IdentityServer's `ITokenService`/`IRefreshTokenService` from a custom endpoint after OTP success
— was prototyped by reflecting the actual `Open.IdentityServer.dll` (2.0.0) API surface and rejected: it needs a
hand-built `Open.IdentityServer.Validation.ValidatedRequest` and `ResourceValidationResult` (normally populated by
IdentityServer's own request-validation pipeline, not meant for manual construction), which is a much larger
surface to get subtly wrong than extending the resource-owner-password-grant validator IdentityServer already
calls. Keeping `/connect/token` as the only place a token is ever minted also means refresh-token redemption
(`grant_type=refresh_token`, already used by the app) needed zero changes.

**Client config change:** `mobile`'s `GrantTypes` changed from `["code"]` to `["password"]` in both
`appsettings.json` and `appsettings.Development.json`; `RedirectUris` were dropped (no longer used).
`RequireClientSecret` stays `false` — a secret compiled into a public mobile binary isn't actually secret, so the
real protections against ROPC abuse are what was already there (PIN lockout) plus what's new here (OTP step-up for
any device that hasn't proven phone possession yet).

**A real pre-existing bug this surfaced and fixed:** `/connect/*` is deliberately tenant-agnostic in
`TenantResolutionMiddleware` (tenant for ROPC comes from the `tenant` form field, not the header), which means
nothing was setting the ambient `ITenantContext` for `/connect/token` requests — and `TenantConnectionInterceptor`
publishes *that* to Postgres as `app.tenant_id` for every RLS-protected query. `SaccoPasswordValidator` resolved
the tenant into a local variable and used it correctly for every explicit `WHERE tenant_id = …` clause, but never
published it to the connection, so **FORCE ROW LEVEL SECURITY silently returned zero rows** regardless of the
explicit filter being right. `SaccoPasswordValidator` now calls `TenantContext.Set(...)` itself right after
resolving the tenant slug, before any DB query in the method. Caught by the new device-trust integration tests,
not by anything pre-existing — worth a mention in `docs/testing/seed-data-strategy.md`'s next revision that ROPC
paths are otherwise under-covered for RLS-correctness specifically.

**SMS interface promoted to a Shared contract:** `ISmsSender` moved from `Sacco.Modules.Notifications.Channels` to
`Sacco.Shared.Notifications`, mirroring `IEmailSender`'s existing pattern — `MemberOtpService` (Identity module)
needed to send SMS without depending on the Notifications module directly, same module-boundary reasoning as
Reporting's PDF digest depending on `IEmailSender`.
