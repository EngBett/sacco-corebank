# SACCO Core Banking Platform

A complete, multi-tenant core banking system for Kenyan deposit-taking SACCOs — members and KYC,
FOSA and BOSA, the general ledger, the full loan lifecycle, mobile money, SASRA reporting, a staff
portal, a tenant-branded website and a member mobile app. Sold as source code.

This document is for two readers: the SACCO decision-maker deciding whether this fits their society,
and the developer deciding whether the code is worth owning. Nothing here is aspirational — where
something is a scaffold, unverified, or still to be confirmed with SASRA, it says so.

---

## What you get

Four applications and the infrastructure to run them, roughly 52,000 hand-written lines:

| Application | What it is |
|---|---|
| **API** | .NET 10 modular monolith — 9 business modules, 201 REST operations, one PostgreSQL database |
| **Staff portal** | Next.js 16 web app, 37 pages, every page gated by permission |
| **Public website** | Next.js 16 marketing site with an online membership application, branded per SACCO |
| **Member app** | Flutter app for Android — 11 screens, phone + PIN sign-in, biometric unlock |

Plus: 29 database migrations, an idempotent seed tool that builds a complete demo SACCO, five mock
payment and SMS gateways so you can run the whole thing without a single production credential,
269 backend tests, Docker Compose, GitHub Actions pipelines, and Terraform for three AWS
environments.

---

## The thinking behind it

Three decisions shape everything else, and each is written up as an architecture decision record in
`docs/architecture/` — 18 of them, explaining *why*, not just *what*.

**FOSA and BOSA are one ledger, not two systems.** Every account, every journal line and every
product carries an explicit FOSA/BOSA tag. A journal must balance overall *and within each segment*;
money crossing between them goes through a clearing pair, so the FOSA and BOSA trial balances each
stand on their own and net to zero when consolidated. You get segment reporting without running two
sets of books and reconciling them by hand.

**Branches are a dimension, not separate books.** Staff, members and journal entries each carry a
branch. You can report and filter by office without consolidating anything. A single-office SACCO
ignores the feature entirely.

**Correctness beats convenience.** Balances change only inside the posting transaction, through
atomic conditional updates taken in a fixed order, so concurrent debits cannot race or deadlock.
There is a load test that hammers the same account from many directions to prove it.

---

## What the system does

### Members and KYC

Register a member, capture personal details and next of kin, upload ID and photo documents, and
verify KYC — with the rule that the person who registered a member cannot be the one who verifies
them. Suspend and reinstate. Assign members to a branch. Exit a member through a controlled
workflow that settles their loans from their deposits, refuses while they still guarantee someone
else, closes their accounts and pays out the balance.

The public website carries a membership application form protected by Cloudflare Turnstile. It
creates a *pending application* for staff review — never a live member. That boundary is enforced in
the API, not in the website.

### Savings, shares and dividends (FOSA and BOSA)

Products for on-demand FOSA accounts, BOSA deposits, share capital and fixed deposits, each with its
own rules. Account numbers read as `M00012-SV`. Deposits are idempotent on the teller's or
provider's reference, so a retried request never doubles a deposit.

Withdrawals follow the SACCO's own limits: teller cash within the product limit pays out in one
step; anything larger becomes a request that a second officer approves, with the money held on the
ledger and the notice period enforced at payout.

Dividends are declared by one officer and approved by another, posted to the ledger and paid net of
withholding tax. Share capital, which a member cannot simply withdraw, can be listed and transferred
to another member through a shares marketplace with staff approval.

A **fee matrix** prices deposits, withdrawals and balance enquiries by channel, product and amount
band — as a flat fee, a percentage with caps, or tiered bands. Fee changes are maker-checker, and
overlapping rules are refused rather than silently applied.

### Loans

The whole lifecycle: products with eligibility multipliers and approval thresholds; application and
appraisal; guarantors; approval; disbursement; repayment; accrual; delinquency; provisioning; and
the awkward endings — write-off, restructuring and settlement on exit.

Two things are worth calling out. **Guarantees are ledger holds**, not a spreadsheet: when a member
guarantees a loan, the amount is held against their BOSA deposits, so their available balance
already reflects the exposure, and caps on guarantee-to-deposit ratio and active guarantee count are
enforced arithmetically. And **approval is N-of-M**: above a product's threshold a committee must
approve, no approver may be the originator or appraiser, and the disburser may not be the
originator.

NPL aging buckets and provisioning rates are configuration rows seeded with the SASRA schedule. A
provisioning run is computed by one user and posted by another.

There is also a **credit scorecard** — a per-SACCO set of weighted factors that scores every
application at capture and again at appraisal, storing the factor-by-factor breakdown. The
recommendation is advisory; it never changes a loan's status on its own.

### General ledger

A real double-entry ledger: chart of accounts, member and loan sub-ledger accounts, journals, and a
posting engine every other module posts through. Manual journals and reversals are maker-checker.
Trial balance for FOSA, BOSA and consolidated. A reconciliation endpoint recomputes running balances
against journal lines and reports any drift — a question an auditor will ask and most systems cannot
answer.

### Payments

M-Pesa, Airtel Money, Equity (Jenga) and NCBA are implemented behind one provider interface, with
collections and disbursements orchestrated by durable sagas that survive a restart and time out into
a manual reconcile queue rather than hanging.

Every write path is idempotent: the provider's transaction reference is claimed in a unique index
*before* any ledger posting, so a webhook delivered three times posts once. Callbacks arrive on a
per-tenant URL and can be restricted to the provider's IP ranges. An unsolicited paybill credit that
matches no account parks in a FOSA suspense account instead of being lost.

*(What has and hasn't been verified against real provider accounts: see "Honest limits" below.)*

### Reporting

Financial position, income statement, capital adequacy, liquidity, portfolio quality and large
exposures — each available on its own, and together as an immutable statutory return package with
**eight reconciliation checks** that must all pass before the return can be submitted. The generator
cannot be the submitter. Historical periods use each loan's ledger balance as of that date, so a
return for a past period reconciles to that day's trial balance.

Every package prints the prudential figures it used and their source, plus a list of open items
still to be confirmed with SASRA — the system never quietly hard-codes a regulatory number.

A **nightly PDF digest** renders the day's key figures into a branded report and emails it to a
recipient list. Board members receive it without needing a portal login.

### Governance: who did what, and proof it wasn't edited

Authorization is permission-based throughout — 58 granular permissions bundled into roles, resolved
server-side on every request so revoking access takes effect immediately rather than when a token
expires.

The audit trail records every consequential action with the actor's id *and their name as it was at
the time*, the outcome (including failures and denials), IP address, user agent, request id and
branch. Entries are hash-chained, and the database itself refuses updates and deletes — the
application's own database user cannot lift that guard. An auditor can filter the trail, export it
as CSV (an act which is itself audited), and run a verification that recomputes the chain and names
the first altered entry if anything was touched.

### Multi-tenancy

One deployment can serve several SACCOs. Isolation is enforced by PostgreSQL row-level security,
forced at the database level, not by remembering to add a `WHERE` clause — a connection with no
tenant bound reads nothing at all. Each SACCO gets its own branding, logos, products, website
content and users.

### Staff accounts and sign-in

Staff are invited, never handed a password. An invitation from a branch manager needs a second
administrator's approval; the new user activates through a single-use emailed link and sets their
own password. Every staff sign-in requires an authenticator-app code (TOTP), enrolled at first
sign-in with recovery codes. Password resets are self-service links. Administrators never see or set
anyone's password.

### The member app

Members sign in with their phone number and a PIN, confirm a new device by SMS OTP, and unlock with
a fingerprint thereafter. They see their accounts and balances, statements with a balance trend,
dividends by year, and the shares marketplace; they can top up by M-Pesa, Airtel Money or Equity, and
request a withdrawal — which lands in the same maker-checker queue a branch request would.

---

## Honest limits

What a buyer should know before committing. None of these are hidden in the code; they are stated in
the repository's own documentation.

**Payment providers are written but not certified.** The M-Pesa, Airtel, Equity and NCBA
integrations are complete code, unit-tested, and driven end to end against mock gateways that mirror
each provider's API. They have never run against a real provider account. Going live means obtaining
credentials, running the provider's UAT, and correcting any drift between the mock and the real
gateway.

**There is no credit reference bureau integration.** The interface exists and a sandbox implementation
always reports "unavailable". Connecting TransUnion, Metropol or Creditinfo is work still to be done.

**SASRA figures are configured defaults, not confirmed ones.** Provisioning percentages, aging
buckets, capital and liquidity minima, the large-exposure threshold and the withholding tax rate are
seeded with sensible values and printed with every return as open items. The SDGF contribution basis
is unset, so it is not computed at all. Mapping the return package onto SASRA's current electronic
submission format has not been done — you get structured JSON and a CSV export.

**It has never run a real SACCO.** Everything is demonstrated against seeded data and sandbox
credentials. The cutover runbook exists; the cutover has not happened.

**The iOS build is unverified.** The Flutter app is verified on Android (analyze, test and a debug
build all pass). The iOS project exists but has not been built or submitted.

**Front-end tests are lint and build only.** The backend has 269 automated tests; the two Next.js
apps do not have an automated test suite.

**SMS: two providers are scaffolds.** Africa's Talking, Twilio and WhatsApp Cloud API are written
against documented, stable APIs. The Safaricom and Airtel SMS senders are starter scaffolds — no
public sandbox exists for either, so confirm the endpoint and payload against your contract before
go-live. Email is plain SMTP, so any provider is a configuration change.

---

## Running it yourself

The whole system runs locally with no production credentials at all:

```bash
docker compose up -d postgres mailpit mocked-sms
dotnet run --project backend/seed/Sacco.Seed      # migrate + seed a complete demo SACCO
dotnet run --project backend/src/Sacco.Api        # API + OIDC + Scalar reference at :5000
```

The seed builds a working society: three branches, seven roles, eight staff users, twenty members
across every KYC state, a full chart of accounts with twelve months of balanced postings, five
savings products across fifty-one accounts, fixed deposits, withdrawal requests awaiting approval, a
declared dividend, six loans — one in each aging bucket — with credit scores, a provisioning run
awaiting approval, a statutory return awaiting submission, payment fixtures including a duplicate
webhook and a timeout, a pending membership application and a pending staff invitation.

In other words, you can open the portal and exercise every workflow within minutes of cloning,
without talking to Safaricom, a bank, or SASRA. That is a deliberate rule of the codebase: swapping
sandbox credentials for production ones is configuration, never a code change.

---

## What a deployment needs

- **Runtime**: .NET 10, Node 20+, PostgreSQL 16, Docker. Redis only if you run more than one API
  instance (SignalR backplane). Chromium for PDF rendering, included in the API image.
- **Accounts you provide**: Safaricom Daraja and/or Airtel Money, a bank API (Equity or NCBA), an
  SMTP provider, an SMS gateway, Cloudflare Turnstile keys for the public site, and a domain per
  SACCO.
- **Team**: the code is conventional .NET and TypeScript with a documented module boundary rule. A
  developer comfortable with ASP.NET Core and Next.js can work in it; the ADRs exist so they inherit
  the reasoning rather than guessing at it.
- **Hosting**: Terraform modules for AWS across dev, staging and production (network, database with
  automated backups and Multi-AZ, application, secrets). Nothing ties the code to AWS.

---

## Documentation included

- 18 architecture decision records — one per structural decision, with the reasoning
- A SASRA requirement-to-implementation mapping, and a register of what still needs confirmation
- A production cutover runbook
- Per-area guides for the backend, front ends, mobile app and infrastructure
- Seed data documentation: every demo login and exactly what each can demonstrate
- An OpenAPI document and a generated TypeScript client, kept in sync by CI
