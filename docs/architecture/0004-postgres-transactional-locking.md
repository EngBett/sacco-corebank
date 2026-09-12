# ADR 0004: Postgres row locking for account debits, not distributed locking

## Status
Accepted

## Context
Concurrent debits against the same member account are a real risk (double-spend / race
condition). Distributed locks (Redis, ZooKeeper, etc.) exist to coordinate independent
systems with their own state — that's not this system's shape, since every write goes
through the same Postgres primary (per ADR 0001).

## Decision
Use Postgres-native concurrency control: an atomic conditional `UPDATE ... WHERE balance >=
@amount` for simple debits, or `SELECT ... FOR UPDATE` inside an explicit transaction when a
debit is part of a larger unit of work (validation, GL posting, guarantor checks). No
external distributed-locking dependency is introduced.

## Consequences
- No new failure mode (a lock service outage cannot halt transaction processing).
- Idempotency for async/retry-prone flows (payment webhooks) is solved separately, via unique
  constraints on the provider's transaction reference — not by locking.
- If the ledger is ever sharded across independent databases (not currently planned), this
  ADR would need revisiting in favor of a saga/compensation pattern rather than a distributed
  lock, per the reasoning in the ADR that would supersede this one.
