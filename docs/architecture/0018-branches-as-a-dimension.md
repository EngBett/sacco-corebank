# ADR 0018: Branches as a dimension, not separate books

## Status
Accepted (2026-09-18)

## Context
Most Kenyan deposit-taking SACCOs run more than one office: a head office and branches, sometimes agency or satellite
outlets. Staff work at one of them, members are served by one of them, and money is taken in over a counter that
belongs to one of them. The platform had no idea any of this existed: every member, every posting and every audit row
looked alike no matter where it happened.

Two shapes were possible:

1. **A set of books per branch** — each office keeps its own ledger and the SACCO consolidates. This is how some legacy
   systems work, and it is what "branch accounting" usually means.
2. **A dimension on the existing single set of books** — one ledger, with each journal, member and user tagged with
   the office it belongs to.

The same question was already settled once in this codebase for FOSA/BOSA (ADR 0002), and the reasons hold here too: a
SACCO's statutory returns, capital adequacy and provisioning are computed for the society as a whole, and reconciling
several sets of books to produce them is where errors live. A branch is a place, not a legal entity.

## Decision
- **A branch is a dimension.** `platform.branches` (code, name, head-office flag, county, town, physical address, phone,
  email, display order, active, opened date) is a tenant-scoped table under forced RLS like every other. There is no
  per-branch ledger, no per-branch trial balance boundary, and no inter-branch clearing account.
- **Exactly one branch is the head office.** Marking another as head office demotes the current one in the same
  transaction (`platform.branch.head_office_active` refuses deactivating the head office). Codes are 2–10 characters,
  upper-cased, and unique per tenant (`platform.branch.duplicate`).
- **`IBranchDirectory` (Sacco.Shared)** is how other modules see branches: list, find by id or code, the default
  (head office), and `EnsureExistsAsync` which throws `platform.branch.unknown`. Modules never touch the Platform
  module's tables — the same boundary rule as everywhere else.
- **Three things carry a branch:**
  - `identity.users.branch_id` — where a staff member works. It is issued in the token as the `branch_id` claim and
    surfaced by `ICurrentUser.BranchId`.
  - `members.members.branch_id` — the office that serves the member. It defaults to the registering user's branch, then
    the head office, and can be moved (`PUT /api/members/{id}/branch`, audited as `members.branch_changed`).
  - `ledger.journal_entries.branch_id` — where the money was taken in or paid out. Postings are stamped with the
    request's `BranchId` when one is given, otherwise with the acting user's branch. A reversal keeps the original
    entry's branch, so a correction lands where the mistake did.
- **Audit rows carry the actor's branch** (ADR 0019), so an office can be audited on its own.
- **Branch is never a permission boundary.** It filters and it reports; it does not decide what a user may do. A teller
  at Eldoret is not stopped from serving a Nakuru member — that is a business rule a SACCO may want later, and it would
  be a permission, not an implicit consequence of this column.
- **Nullable everywhere.** A single-office SACCO ignores branches entirely, and a SACCO that adds offices later does not
  have to backfill history before the feature works.
- **Public:** `GET /api/public/branches` lists the active offices for the website's contact page, under the `public-read`
  limit.

## Consequences
- Branch reporting is a `GROUP BY branch_id` over the existing ledger, not a consolidation exercise. Nothing in the
  trial balance, capital adequacy or provisioning code changes.
- Journals posted before this ADR have a null branch. They are reported as unassigned rather than guessed at.
- `POST /api/ledger/postings` gained an optional `branchId`, so a module posting on behalf of a branch (a mobile-money
  deposit received at a branch counter) can say so explicitly.
- The demo tenant seeds a head office and two branches (Nakuru, Eldoret), spreads demo members and staff across them,
  and backfills any member or user seeded before branches existed.
- Still open: per-branch cash accounts (a till per office) and branch-level limits. Both are additive on top of this
  dimension; neither requires separate books.
