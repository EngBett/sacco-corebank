# icodeio product listing — copy-paste content

Every heading below is a field on the icodeio `Product` (and `Edition`) model, in the order the admin
form and the product page use them. Copy the block under each heading as-is.

Two formatting rules taken from the icodeio front end:

- **`FullDescription` renders as plain text** (`whitespace-pre-line`) — paragraphs separated by blank
  lines, no markdown syntax. It is written that way below.
- **`GatedDetailsMarkdown` renders as markdown** and is only ever shown to someone with an approved
  access grant. Never returned by the public endpoint.

---

## Name

```
SACCO Core Banking Platform (Kenya, SASRA-aligned)
```

## Slug

```
sacco-core-banking-platform
```

## ProductType

`SourceCode`

## SalesMotion

`RequestAccess` — recommended.

This is a regulated-buyer product: a SACCO's board will want a demo, a licence negotiation, an
implementation plan and probably source escrow before money changes hands. `RequestAccess` keeps
prices and editions off the page, refuses checkout server-side, and captures a qualified lead instead.
Switch to `SelfServe` later if you decide to sell a developer edition off the shelf.

## ShortDescription

```
A complete core banking system for Kenyan deposit-taking SACCOs: members and KYC, FOSA and BOSA, a double-entry ledger, the full loan lifecycle, mobile money, SASRA reporting, a staff portal, a branded website and a member mobile app.
```

## FullDescription

*(plain text — paste exactly, blank lines included)*

```
Everything a deposit-taking SACCO runs on, as source code you own: member onboarding and KYC, FOSA and BOSA savings, share capital and dividends, the full loan lifecycle from application to write-off, a real double-entry general ledger, M-Pesa and bank integrations, SASRA-aligned reporting, and an append-only audit trail.

Four applications ship together. A .NET 10 API with 201 REST operations across nine business modules. A 37-page staff portal in Next.js where every page is gated by permission. A tenant-branded public website with an online membership application. A Flutter member app for phone-and-PIN self-service with biometric unlock.

It is built for the way a SACCO is actually regulated. FOSA and BOSA are one ledger with a segment tag on every account and journal line, so each trial balance stands on its own and nets to zero consolidated — not two systems reconciled by hand. Every money-moving approval is maker-checker, enforced in code. Authorization is 58 granular permissions resolved server-side, so revoking access takes effect immediately. The audit trail is hash-chained and the database itself refuses to let anyone edit or delete it.

One deployment can serve several SACCOs. Tenant isolation is enforced by PostgreSQL row-level security, forced at the database level, so a connection with no tenant bound reads nothing at all.

The whole system runs on your laptop with no production credentials. A seed tool builds a complete demo society — three branches, twenty members across every KYC state, twelve months of balanced postings, six loans across every aging bucket, a provisioning run and a statutory return awaiting approval — and five mock gateways stand in for M-Pesa, Airtel Money, Equity, NCBA and SMS. Swapping sandbox credentials for production ones is a configuration change, never a code change.

Included: full source for all four applications, 29 database migrations, 269 backend tests, a k6 load test for concurrent debits, Docker Compose, GitHub Actions pipelines, Terraform for three AWS environments, 18 architecture decision records explaining why each structural decision was made, a SASRA requirement mapping and a production cutover runbook.
```

## Features

*(each line is one array item)*

```
Double-entry general ledger with FOSA/BOSA segment tagging on every account and journal line
Concurrency-safe posting engine — atomic conditional updates, no distributed lock, load-tested
Member onboarding, KYC verification with document capture, suspension, and a controlled exit workflow
Savings, share capital, fixed deposits and dividends, with idempotent deposits and notice-period withdrawals
Fee matrix — flat, percentage or tiered pricing by channel, product and amount band
Full loan lifecycle: application, appraisal, guarantors, N-of-M approval, disbursement, repayment and accrual
Guarantees enforced as ledger holds against the guarantor's deposits, with ratio and count caps
NPL aging and loan-loss provisioning, computed by one officer and posted by another
Per-SACCO credit scorecard with a stored factor-by-factor breakdown on every application
Loan write-off, restructuring and settlement from deposits on member exit
M-Pesa, Airtel Money, Equity (Jenga) and NCBA integrations behind one provider interface
Idempotent payment webhooks — the provider reference is claimed before any ledger posting
SASRA-aligned statutory returns with eight reconciliation checks that must pass before submission
Nightly branded PDF digest emailed to board members without a portal login
Append-only, hash-chained audit trail the database itself refuses to edit or delete
Permission-based authorization — 58 permissions, resolved server-side, revocation is immediate
Maker-checker on every money-moving approval, enforced in code and covered by tests
Multi-tenant by design, isolated with forced PostgreSQL row-level security
Branches as a reporting dimension on one set of books, not separate books
Staff onboarding by invitation with mandatory authenticator-app two-step verification
Real-time in-app notifications over SignalR, plus an SMS and email outbox with retry
Tenant-branded public website with a Turnstile-protected membership application
Flutter member app: phone and PIN sign-in, SMS OTP on a new device, biometric unlock
Complete demo data and five mock gateways — run every workflow with zero production credentials
```

## Requirements

```
.NET 10 SDK and PostgreSQL 16
Node.js 20+ for the staff portal and public website
Flutter 3.8+ to build the member app (Android verified; iOS project included but unbuilt)
Docker and Docker Compose to run the stack locally
A Safaricom Daraja and/or Airtel Money account for live mobile money
A bank API account (Equity Jenga or NCBA) for live bank transfers
An SMTP provider for email and an SMS gateway account for messaging
Cloudflare Turnstile keys for the public membership form
A developer comfortable with ASP.NET Core and Next.js to deploy and maintain it
```

## SupportedPlatforms

```
Linux (Docker)
AWS (Terraform modules for dev, staging and production included)
Any PostgreSQL 16 host
Web (modern browsers)
Android
```

## WhatIsIncluded

```
Full source for all four applications — API, staff portal, public website and Flutter member app
Nine backend modules, 201 REST operations, and a committed OpenAPI document
29 EF Core database migrations
Seed tool that builds a complete demo SACCO, idempotent and re-runnable
Five mock gateways (M-Pesa, Airtel Money, Equity, NCBA, SMS) with an end-to-end script
269 backend tests (174 unit, 95 integration) and a k6 load test for concurrent debits
Docker Compose for the whole stack and Dockerfiles for every service
GitHub Actions pipelines for backend, frontend and infrastructure
Terraform modules for three AWS environments, with backups and Multi-AZ in production
18 architecture decision records explaining the reasoning behind each structural decision
SASRA requirement-to-implementation mapping and an open-items confirmation register
Production cutover runbook and per-area developer guides
Generated TypeScript API client, kept in sync with the backend by CI
```

## WhatIsNotIncluded

```
Production payment credentials — the live M-Pesa, Airtel, Equity and NCBA code is written and driven against mock gateways, but has not been certified against a real provider account
Credit reference bureau integration — the interface exists; connecting TransUnion, Metropol or Creditinfo is still to be done
SASRA electronic submission format — returns are produced as structured JSON and CSV; mapping onto the regulator's portal format is not done
Confirmed prudential figures — provisioning rates, aging buckets, capital and liquidity minima ship as configurable defaults flagged as open items
SDGF contribution computation — the basis is unset, so it is not calculated
A verified iOS build or app store submission — Android is verified, the iOS project is untested
Automated front-end test suite — the two Next.js apps are covered by lint and build only
Hosting, domains, SSL certificates and managed database costs
Data migration from your existing core banking system
Regulatory sign-off, licensing or SASRA approval on your behalf
```

## Technologies

```
.NET 10
ASP.NET Core
Entity Framework Core
PostgreSQL
Next.js 16
React 19
TypeScript
Tailwind CSS
Flutter
Dart
SignalR
OpenID Connect
Wolverine
Docker
Terraform
```

## Tags

```
sacco
core-banking
fintech
kenya
sasra
microfinance
double-entry-ledger
multi-tenant
mpesa
loan-management
saas
```

## Categories

Map to your existing taxonomy — most likely **Fintech** plus **Full-Stack Applications** (or whichever
category currently holds the payments-infrastructure product, since this sells the same way).

## Version / ReleaseDate

```
Version:      1.0.0
ReleaseDate:  the date you publish the listing
```

## Changelog

```
1.0.0 — First release. Nine backend modules, staff portal, public website and Android member app. Branch dimension, hash-chained audit trail and demo mode added ahead of release.
```

## LicenseOptions and editions

`RequestAccess` hides prices, so editions mainly shape the conversation. A structure that fits this
product:

| Edition | Licence | What it buys |
|---|---|---|
| Single SACCO | `Enterprise` | One deposit-taking society, one production deployment, 12 months of updates |
| Multi-tenant operator | `Enterprise` | Run several SACCOs on one deployment — for a service bureau or a union |
| Source + implementation | `Enterprise` | The above plus setup, provider certification and cutover support |

Price is your call and belongs in the negotiation, not on the page. Two things worth deciding before
your first lead: whether updates are 12 months or perpetual, and whether you offer source escrow —
a SACCO board will ask, and the answer is easier given up front than negotiated late.

## MetaTitle

```
SACCO Core Banking Platform — Source Code for Kenyan Deposit-Taking SACCOs
```

## MetaDescription

```
Complete core banking source code for Kenyan SACCOs: members and KYC, FOSA and BOSA, double-entry ledger, loans with guarantors, M-Pesa integration, SASRA-aligned reporting, staff portal, branded website and a member mobile app.
```

## FAQs

**Is this SASRA compliant?**
It is built around SASRA's requirements and ships a requirement-by-requirement mapping document:
segregation of duties, maker-checker on every money-moving approval, an append-only audit trail,
FOSA/BOSA separation, NPL aging and provisioning, and the statutory return package with eight
reconciliation checks. What it does not do is claim approval on your behalf. Nine prudential figures
— provisioning rates, aging buckets, capital and liquidity minima, the large-exposure threshold, the
withholding tax rate and the SDGF basis — ship as configurable defaults that every generated return
prints as open items, to be confirmed with SASRA before your first live submission. Mapping the
return onto SASRA's electronic portal format is work still to be done.

**Does it integrate with M-Pesa?**
Yes — Safaricom Daraja (STK push and B2C), Airtel Money, Equity's Jenga DFS and NCBA are all
implemented behind one provider interface, with idempotent webhook handling so a callback delivered
three times posts once. The code is complete and driven end to end against mock gateways that mirror
each provider's API, but it has not been certified against a live provider account. Going live means
your credentials, the provider's UAT, and correcting any drift between the mock and the real gateway.

**Can one deployment serve more than one SACCO?**
Yes. Multi-tenancy is built in, and isolation is enforced by PostgreSQL row-level security forced at
the database level — a connection with no tenant bound reads nothing at all, so isolation does not
depend on a developer remembering a `WHERE` clause. Each SACCO gets its own branding, products,
website content and users.

**Do I get the mobile app source too?**
Yes, the complete Flutter source. It is verified on Android — analyze, tests and a debug build all
pass. The iOS project is included but has not been built or submitted to the App Store, so budget
for that if you need iPhone.

**How do members and staff sign in?**
Staff are invited by email, never handed a password, activate through a single-use link, set their
own password, and must enrol an authenticator app — every staff sign-in needs a TOTP code. Members
sign in to the mobile app with their phone number and a PIN, confirm a new device by SMS OTP, and
unlock with a fingerprint afterwards.

**Can we white-label it for our SACCO?**
Yes. Logos (including separate light and dark variants), favicon, colours, name and tagline are
tenant settings edited in the portal, and the public website and mobile app pick them up from the
API. The website's products and services pages are also edited from the portal, so your team
publishes content without a developer.

**What about credit reference bureau checks?**
The interface is there and every application is scored through a per-SACCO credit scorecard, but the
bureau implementation is a sandbox that always reports "unavailable". Connecting TransUnion, Metropol
or Creditinfo is work still to be done — flagged here rather than discovered later.

**Can I evaluate it before buying?**
Yes, that is what the demo is for. The entire system runs on a laptop with no production credentials:
one command seeds a complete society — twenty members, twelve months of postings, loans in every
aging bucket, a provisioning run and a statutory return awaiting approval — and five mock gateways
stand in for the payment and SMS providers.

**What does my team need to run it?**
PostgreSQL 16 and Docker, plus a developer comfortable with ASP.NET Core and Next.js. Terraform
modules for AWS are included if you want a managed deployment, though nothing ties the code to AWS.
The 18 architecture decision records exist so whoever maintains it inherits the reasoning instead of
guessing at it.

**Has it run a real SACCO yet?**
No. Everything is demonstrated against seeded data and sandbox credentials, and the cutover runbook
is written, but the platform has not yet gone live at a society. The first deployment is a joint
implementation, and the licence conversation should reflect that.

---

## Suggested images

The listing needs a cover image and a gallery. Screenshots that sell this product, in order:

1. Portal dashboard — the first thing a CEO sees
2. Loan detail with the approval trail and guarantors
3. Trial balance or the reconciliation view — the auditor's question, answered
4. Audit trail with the verify-chain result showing the trail intact
5. The statutory return package with its eight reconciliation checks
6. The member mobile app — sign-in and the accounts dashboard, side by side
7. The tenant-branded public website home page

Use the seeded demo SACCO for all of them; the data looks real because it is internally consistent.

## GatedDetailsMarkdown

*(markdown — shown only to an approved access grant)*

```markdown
## Architecture

A modular monolith on .NET 10: one solution, one PostgreSQL database, nine modules with strict
boundaries enforced by project references. Modules talk through interfaces in a shared contracts
project — never each other's tables or DbContexts — so a module can later be extracted without a
rewrite. Each module owns one Postgres schema and its own DbContext.

The two web apps are Next.js 16. The portal uses a BFF: the browser never holds an API token, and
the session is an encrypted cookie. Business rules live in the .NET backend only; the BFF is a thin
session and translation layer.

Messaging is Wolverine (MIT) for the two payment sagas, with durable Postgres state. Authentication
is an in-process OIDC authorization server; the portal uses authorization code with PKCE.

~52,000 hand-written lines: 14,400 backend modules, 8,700 portal, 6,800 Flutter, 5,900 tests, 3,700
migrations, 2,100 public site, 1,600 seed tool, 1,000 Terraform.

## Security posture

- Permissions resolved server-side per request (60-second cache, invalidated on change) — never
  baked into tokens, so revocation is immediate
- Forced row-level security for tenant isolation, with an integration test asserting an untenanted
  connection reads nothing
- TOTP secrets encrypted with AES-256-GCM under a key ring; recovery codes stored hashed
- Activation and reset links are single-use and hashed at rest (72 hours and 60 minutes)
- Lockout after five failed attempts, shared between password and TOTP, so alternating guesses
  cannot dodge it
- Payment webhooks are anonymous by necessity, rate-limited, and can be restricted to the provider's
  IP ranges; the source-IP guard warns at startup if left empty in production
- The staff password grant is refused outright in production
- Audit rows are hash-chained per tenant, and the append-only guard is a database trigger the
  application's own database role cannot disable

## Verification status

| Area | Status |
|---|---|
| Backend | 174 unit + 95 integration tests, all passing; integration tests run against real PostgreSQL |
| Concurrency | k6 load test driving concurrent debits against one account |
| Payment providers | Unit-tested against a fake HTTP handler; end-to-end against five mock gateways; **not certified against live provider accounts** |
| Front ends | Lint and build in CI; no automated test suite |
| Mobile | Android verified (analyze, test, debug build); **iOS unbuilt** |
| Compliance | Internal sign-off with two findings open (live provider credentials, SASRA figures); **no independent review** |

## Implementation path for a first SACCO

1. Provision infrastructure (Terraform modules included) and deploy the API, portal and public site
2. Load the chart of accounts and configure products, fee rules and approval thresholds
3. Obtain provider credentials, run each provider's UAT, correct any mock-to-live drift
4. Confirm the nine prudential figures with SASRA and set them in configuration
5. Migrate member, savings and loan balances — the largest unknown, and not included in the product
6. Train staff on the maker-checker workflows; enrol two-step verification
7. Parallel-run against the existing system for one reporting cycle before cutting over

Realistically, a first implementation is a joint project, not a handover.

## Commercial terms to agree

- Licence scope: one society, or a multi-tenant operator
- Update window: 12 months or perpetual
- Source escrow: available on request — worth offering before the board asks
- Implementation support: scoped separately from the licence
- Who carries responsibility for regulatory sign-off (the SACCO, always — state it in writing)
```
