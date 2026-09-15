# ADR 0008: Mobile scope — member self-service app first, field-agent app deferred

## Status
Accepted (2026-09-14) as the direction for the initial release: the member self-service **API** described below is
built, tested and seeded (`/api/self/*`, phone + PIN logins, the `mobile` PKCE client). The app itself is the next
deliverable; the product owner should ratify the choice of React Native versus a responsive web app before it starts.

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
