# ADR 0003: Wolverine for messaging/sagas, not MassTransit

## Status
Accepted

## Context
MassTransit v9 (shipped January 2026) moved to a commercial/source-available license
(~$400–1200/month, with an ambiguous "may qualify" free tier for orgs under $1M revenue).
MassTransit v8 remains Apache 2.0 but the vendor has only committed to security patches
through end of 2026, with no committed support horizon beyond that. Starting a new,
multi-year regulated financial system on a dependency with that licensing trajectory is a
real risk.

## Decision
Use Wolverine (MIT-licensed, actively developed) for messaging and sagas instead. Scope
sagas to genuinely long-running, multi-step async workflows: payment provider collection/
disbursement orchestration, batch payroll check-off processing. Core ledger/member/loan
transactions stay as plain synchronous Postgres transactions — no message bus involved.

## Consequences
- No licensing overhang or forced migration risk from a vendor's commercial pivot.
- Smaller community/ecosystem than MassTransit — mitigated by keeping saga usage narrowly
  scoped rather than using it as a general application pattern.
- Revisit this ADR only if Wolverine's own trajectory changes, or if a specific feature gap
  is discovered that blocks a real requirement.
