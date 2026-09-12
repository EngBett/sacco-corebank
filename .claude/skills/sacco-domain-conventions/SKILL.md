---
name: sacco-domain-conventions
description: Use whenever implementing or modifying any backend domain module (ledger, members, savings, lending, reporting). Encodes the FOSA/BOSA tagging rule, maker-checker requirement, and permission-based authorization convention that every domain change must follow. Trigger on any change touching accounts, transactions, approvals, or GL entries.
---

# SACCO domain conventions

Apply these rules to every domain-level change in `backend/src/Sacco.Modules.*`.

## FOSA/BOSA tagging
Every account and every transaction line carries an explicit FOSA or BOSA tag. This is a
dimension on the shared GL, not a separate ledger (ADR 0002). Before adding a new account
type or transaction type, confirm which tag it carries and that GL postings preserve it.

## Maker-checker
Any action that moves member money or changes capital position (loan disbursement/approval,
GL adjustment, dividend declaration, share withdrawal on exit) must record `InitiatedByUserId`
and require a distinct `ApprovingUserId` before taking effect. For larger amounts, model
N-of-M committee approval rather than a single approver — check the relevant loan/GL policy
threshold rather than assuming single-approver is always sufficient.

## Permission-based authorization
Check granular permissions (e.g. `loans.approve`, `members.kyc.verify`) via ASP.NET Core
policy handlers. Never branch on role name in domain or application code. Role → permission
mapping is configuration, editable per tenant without a deployment.

## Concurrency
Use Postgres row locking for balance-affecting operations — `SELECT ... FOR UPDATE` inside an
explicit transaction, or a single atomic conditional `UPDATE ... WHERE balance >= @amount`.
Never introduce a distributed lock (Redis, ZooKeeper, etc.) for this (ADR 0004).

## Every change needs seed data
See the `seed-data-generation` skill — it is a companion, not a substitute, for this one.
