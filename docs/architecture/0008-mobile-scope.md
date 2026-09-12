# ADR 0008: Mobile scope — member self-service app first, field-agent app deferred

## Status
Proposed — awaiting product-owner confirmation (see `mobile/CLAUDE.md`). Nothing under `mobile/` is built until this is accepted.

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
