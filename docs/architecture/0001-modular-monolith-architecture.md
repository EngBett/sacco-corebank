# ADR 0001: Modular monolith, not microservices

## Status
Accepted

## Context
This is a regulated financial system with strict consistency requirements (loan
disbursement, guarantor locking, GL posting must be atomic) and, at least initially, a small
team. Microservices would require distributed-transaction patterns (sagas/outbox everywhere)
to fake the atomicity a single database transaction gives for free, plus operational overhead
(service discovery, distributed tracing, per-service CI/CD) not justified at this stage.
Service boundaries are also not yet well understood enough to split confidently.

## Decision
Build one .NET solution, one Postgres database, with strict internal module boundaries
enforced by project references (a module may only depend on another module's public
interface). Peripheral, genuinely async concerns — payment provider callbacks, notifications,
batch check-off processing — may run through the message bus (Wolverine) but the core
member/ledger/lending domain stays synchronous and transactional.

## Consequences
- Core financial transactions get Postgres ACID guarantees for free.
- SASRA audit trail is a single, coherent system to reason about.
- A later split into real services (if genuinely needed by scale or team growth) is a
  boundary-preserving extraction, not a rewrite, because the seams already exist as project
  references.
- We must be disciplined about not letting modules reach into each other's internals — this
  is a convention enforced by code review and project reference rules, not by a runtime
  boundary, so it requires more discipline than microservices would.
