# Seed data

This is the single source of demo/test data for the whole platform. It must be idempotent
(safe to re-run against a fresh database) and must fully populate the "Demo SACCO" tenant.

## What must exist after seeding

- **Tenant**: Demo SACCO, with branding (logo placeholder, brand colors, name) set so the
  portal and public site render correctly out of the box.
- **Users**, one per role at minimum: teller, loan officer, credit committee member, branch
  manager, compliance officer, system admin. Document demo credentials in this file once
  auth is implemented (never commit real secrets — these are sandbox-only demo accounts).
- **Members**: a spread across KYC states — pending, verified, suspended — with realistic
  (but fake) Kenyan names and ID numbers in a clearly-fake range.
- **Chart of accounts**: FOSA and BOSA tagged, covering savings, shares, loan products, and
  the GL control accounts needed for a trial balance to actually balance.
- **Products**: at least one FOSA transactional product, one BOSA savings product, one fixed
  deposit product, one loan product with a guarantor requirement.
- **Loans**: one in each NPL aging bucket (normal, watch, substandard, doubtful, loss), one
  with multiple guarantors near their exposure cap, one pending maker-checker approval.
- **Payment provider fixtures**: for M-Pesa, Airtel Money, and bank transfer each — one
  successful collection, one failed/timed-out collection, one duplicate webhook delivery
  (to prove idempotency), one successful disbursement.
- **A pending public membership application** (to demo the Turnstile → pending-review flow).

## Running it

```bash
docker compose up -d postgres                                  # local Postgres (repo root)
dotnet run --project backend/seed/Sacco.Seed                   # create db if missing, migrate, seed (idempotent)
dotnet run --project backend/seed/Sacco.Seed -- --reset        # drop + recreate first
SACCO_CONNECTION="Host=...;Database=...;Username=...;Password=..." dotnet run --project backend/seed/Sacco.Seed
```

The seeder is deterministic: ids come from `Ids.Deterministic("<kind>:<key>")` and the seed clock
is pinned to `SeedClock.Anchor` (2026-09-11), so aging buckets, statements and tests are stable.
Integration tests run exactly this seeder against a Testcontainers Postgres.

## Demo identities

| Key | Where used |
|---|---|
| Tenant `demo` (`DemoTenant.Id`) | `X-Tenant: demo` header or `demo.<host>` subdomain |
| `DemoTenant.Users.*` | Stable user ids for the staff accounts below |
| `DemoTenant.MemberId("M00001")` … `M00020` | 20 members in `KenyanNames.Members` with `-SH` (shares), `-SV` (BOSA deposits) and `-FO` (FOSA current) ledger accounts |
| `MJ-2026-0007` | Manual journal left pending approval (maker: accountant) — approve as branch manager to demo maker-checker |
| Withdrawals (3) | Approve as `manager`, pay as `teller` (`/api/savings/withdrawals`). The M00004 BOSA one cannot be paid until its 60-day notice expires |
| FY2025 dividend | Declared by `accountant`; approve then pay as `manager` (`/api/savings/dividends`) |
| Loans | Appraise as `loanofficer`, approve as `committee1`/`committee2` (150k loan needs both), disburse as `manager`, repay as `teller` (`/api/loans`) |
| Portal / public site | `cd frontend/portal && npm run dev` (http://localhost:3000, sign in as any user above) and `cd frontend/public-site && npm run dev` (http://localhost:3001, submit a membership application with the always-pass Turnstile test key; it lands in the portal's Applications queue) |
| Payments | Initiate an STK push as `teller` (`POST /api/payments/collections`), then deliver the sandbox callback (`POST /api/payments/sandbox/transactions/{id}/callback`) or wait 5 s in Development |
| Provisioning run | Computed by `accountant` as of the seed date; approve as `manager` to post the loan-loss provision (`/api/loans/provisioning/runs`) |

## Demo staff logins (sandbox only — never in production)

All accounts use the password `Demo2026!pass` (constant `IdentitySeeder.DemoPassword`).

| Username | Role | What they can demo |
|---|---|---|
| `admin` | System Admin | Users, roles/permission bundles, tenant branding, audit log |
| `teller` | Teller | Deposits, withdrawals, repayments, payments |
| `loanofficer` | Loan Officer | Register members, originate/appraise loans |
| `committee1`, `committee2` | Credit Committee | Loan approval (N-of-M) |
| `manager` | Branch Manager | Approve journals/withdrawals/disbursements, review membership applications, suspend members |
| `accountant` | Accountant | Create manual journals (maker), chart of accounts, products, provisioning config |
| `compliance` | Compliance Officer | Verify KYC (checker), audit log, statutory reporting |

Get a token from the command line (password grant, first-party demo client only):

```bash
curl -s -X POST http://localhost:5000/connect/token \
  -d "grant_type=password&client_id=sacco-cli&client_secret=sacco-cli-dev-secret&tenant=demo" \
  -d "username=manager&password=Demo2026!pass&scope=openid profile tenant sacco-api offline_access"
```

Browser login (authorization code + PKCE, used by the portal BFF): `http://localhost:5000/account/login?tenant=demo`.

## Credit scoring

The demo scorecard is the platform default (100 points across eight factors; approve ≥ 70, refer ≥ 50, decline when
CRB-listed). Every seeded loan carries its scores. The sandbox bureau keys off the national ID's last digit, so
`M00010` (ID …10) is CRB-listed — his seeded loan (`LN-000005`) shows a **Decline** recommendation that the committee
overrode, and `M00009` (ID …09) gets an "unavailable" bureau result, which turns her approve-band score into **Refer**.
Sign in as `manager` to edit the scorecard at `/loans/scoring`; as `loanofficer` to recompute a score on any open application.

## Notifications

Every demo user signs in to an inbox: the seeders drive the real workflows, so the pending journal notifies the
checkers, the approved loans notify the loan officer, and so on. `NotificationsSeeder` adds a welcome note per user and
marks the older half of each inbox read, leaving something unread for every role. Trigger a live one while the portal is
open: as `accountant`, create a manual journal — `manager` (holder of `ledger.journal.approve`) sees it arrive in the bell
without a refresh.

## Status by phase

- [x] Phase 1 — tenant, chart of accounts (FOSA/BOSA tagged), member sub-ledger accounts, 12 months of balanced postings, pending manual journal
- [x] Phase 2 — 7 roles, 8 staff users, 20 members (16 verified, 2 pending KYC, 1 suspended, 1 rejected), 1 pending public application
- [x] Phase 3 — 5 products (FOSA current, BOSA deposits, shares, FD 6m/12m), 51 accounts, 2 fixed deposits, 3 withdrawal requests (BOSA notice, FOSA over teller limit, M-Pesa payout), FY2025 dividend declaration pending approval
- [x] Phase 4 — 3 loan products (Development, Emergency, FOSA Salary Advance), SASRA provisioning schedule, 6 disbursed loans (one per aging bucket + a FOSA advance), interest accruals, 1 loan awaiting appraisal, 1 large loan awaiting its 2nd committee approval, M00003 near her guarantor cap, provisioning run pending approval
- [x] Phase 6 — statutory return for the last completed quarter, generated by `accountant`, reconciled, awaiting submission by `compliance`
- [x] Phase 5 — per provider (M-Pesa, Airtel Money, sandbox bank): successful collection (posted), duplicate webhook ignored, failed collection, timed-out collection; plus a successful M-Pesa payout of an approved withdrawal
