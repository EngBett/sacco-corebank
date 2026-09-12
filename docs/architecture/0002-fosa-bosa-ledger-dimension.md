# ADR 0002: FOSA/BOSA as a GL dimension, not two ledgers

## Status
Accepted

## Context
SASRA requires DT-SACCOs to distinguish FOSA (on-demand, bank-teller-style) activity from
BOSA (traditional, notice-period) activity in reporting. The naive approach — two separate
ledgers/systems — breaks down because a single member is one entity across both, loan
eligibility is computed from BOSA balances while disbursement often lands in a FOSA account,
and SASRA also needs a *consolidated* capital-adequacy and liquidity position across both.

## Decision
One core ledger. Every account and every transaction line carries an explicit FOSA or BOSA
tag as a GL dimension. Reporting slices the same ledger by tag (FOSA statement, BOSA
statement) and rolls up to a consolidated statement for capital adequacy/liquidity
computation.

## Consequences
- No reconciliation step is needed between "two systems" — there's only one ledger.
- Product configuration must classify every product as FOSA or BOSA up front.
- Cross-boundary flows (loan disbursed from a BOSA-originated approval into a FOSA account)
  are modeled as normal transactions with per-line tags, not as inter-system transfers.
