# ADR 0019: An elaborate, tamper-evident audit trail

## Status
Accepted (2026-09-18)

## Context
`IAuditLogger` existed from Phase 1 and modules called it at approvals and other hand-offs, but what it recorded was
thin: action, entity, entity id, actor id, an optional free-text detail string. Three things were missing for a SASRA
governance trail, and each of them is the first thing an examiner or a forensic reviewer asks for:

- **Context.** Who is "actor `9f3c…`"? From which address, under which request, at which office? A row that can only be
  read by joining to a live `users` table is worth little once that user is renamed or removed.
- **Failures.** The trail recorded what succeeded. Refused sign-ins, lockouts, blocked webhooks and denied approvals are
  the entries an investigation actually wants.
- **Evidence that it hasn't been edited.** An audit table that the application can `UPDATE` is a log, not a trail.

## Decision
- **Every row carries its context.** `AuditLogEntry` gained `ActorName` (the name as it was at the time), `Outcome`,
  `IpAddress`, `UserAgent`, `BranchId`, plus `PreviousHash`/`Hash`. Context is not passed by each caller: `IAuditContext`
  is resolved per request (`HttpAuditContext` in the API host; `NullAuditContext` for seeding and schedulers) and the
  Platform module stamps it on.
- **Outcome is part of the event.** `AuditOutcome` is `Success`, `Failure` or `Denied`. Failed sign-ins, failed
  two-step verification, lockouts and blocked payment callbacks are recorded, not just their successful counterparts.
- **Details are built, not interpolated.** `AuditDetails` produces the JSON: `With` for plain values, `Changed` for
  `{"field":{"from":…,"to":…}}` (unchanged fields are skipped), `Diff` for `{"name":{"added":[…],"removed":[…]}}`.
  A builder with nothing in it serialises to null, so an empty object is never stored.
- **Details are stored as text, not `jsonb`.** Postgres normalises `jsonb` (key order, whitespace), so a row would come
  back a different string than the one that was hashed and every verification would report tampering. Text also lets the
  search run `ILIKE` over the details.
- **The rows are chained.** Each entry hashes its own fields prefixed with the previous entry's hash for that tenant
  (SHA-256 over ``-joined values; ids are UUIDv7 so chain order matches id order). Writes take a
  transaction-scoped `pg_advisory_xact_lock` keyed on the tenant, so two concurrent actions cannot chain onto the same
  predecessor and fork the chain. Other tenants are never blocked by it.
- **The database refuses edits.** A `BEFORE UPDATE OR DELETE` trigger on `platform.audit_log` raises. The application's
  database role is not the table's owner, so it cannot disable the trigger either — only a database administrator can,
  and if they do, `GET /api/admin/audit-log/verify` recomputes the chain and reports the first entry whose contents no
  longer match. Entries written before chaining existed carry an empty hash and are reported as `unchained`, which is
  not the same claim as "tampered".
- **It can be searched and taken away.** `GET /api/admin/audit-log` filters by action prefix (`savings.` matches every
  savings action), entity type and id, actor, branch, outcome, date range and free text, with paging;
  `GET /api/admin/audit-log/export.csv` returns the same rows as CSV — and the export is itself an audited event.
  Both are behind `admin.audit.view`.
- **Coverage is the point.** Beyond the maker-checker hand-offs already covered: sign-in success, failure and lockout,
  two-step verification failures and resets, recovery-code use, branch changes, deposits, savings and loan product
  changes and listing changes, role permission changes (as diffs), branding changes (as field diffs), maintenance runs,
  statutory return exports, digest downloads and sends, and blocked or received payment webhooks.

## Consequences
- The trail is append-only in the database, not merely by convention, and can be shown to be intact without trusting the
  application that wrote it.
- Verification is O(n) over the window being checked. It is an on-demand action, not something a page render does.
- Audit writes now take a per-tenant advisory lock, serialising them within a tenant. They are small inserts at the end
  of a workflow; if this ever becomes a contention point the answer is batching, not dropping the chain.
- `ActorName` is denormalised on purpose: the trail must stay readable after a user is renamed or removed.
- Rows written before this change keep an empty hash forever. They are counted and reported as unchained; back-filling
  hashes for them would be inventing evidence.
- The portal has an audit page (filters, paging, details, CSV export, verify), and SASRA-facing documentation points at
  it as the governance trail.
