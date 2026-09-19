# ADR 0015: Fee matrix for deposits, withdrawals and balance enquiries

## Status
Accepted (2026-09-17)

## Context
Until now the only charge in the system was a single fixed `WithdrawalFee` per savings product. A SACCO prices
transactions far more finely than that: M-Pesa withdrawals on a banded tariff, bank transfers as a percentage with a
floor and a ceiling, a flat handling fee on check-off remittances. The product owner shared a payments-company fee
matrix (channel × business × amount range × matrix type × risk × charge type × value × min/max cap × status) as the
shape to follow, and asked for Fixed, Tiered and Percentage charges on deposits and withdrawals.

Two related asks came with it:
1. Admins can put a fee on *viewing* the balance of some accounts (a balance-enquiry fee, common for FOSA accounts).
2. Members can choose to hide balances in the app until they re-enter their PIN.

## Decision

### A fee rule is a row in a per-tenant matrix
`savings.fee_rules` (Savings module): transaction type (`Deposit`, `Withdrawal`, `BalanceEnquiry`), optional channel
(`Cash`, `MPesa`, `AirtelMoney`, `BankTransfer`, `CheckOff`; null = any), optional product code (null = all), an amount
range, and a charge: `Fixed` (an amount), `Percentage` (basis points, with optional min/max caps) or `Tiered` (bands with
strictly increasing upper limits, the last one optionally open-ended, with optional caps). Fees are credited to a named
**income** GL account, checked against the chart when the rule is created. The rule stores that GL account's segment, so
a fee taken from a BOSA account into a FOSA income account is bridged through the clearing pair like any other
cross-segment movement (ADR 0002).

Mapping from the reference matrix:
- **Business** becomes the tenant, since every rule is already tenant-scoped.
- **Matrix type** is the transaction type.
- **Risk** is not modelled. There is no member risk class yet, and adding one only for pricing would be speculative.

### Resolution: most specific rule wins, otherwise today's behaviour
When a rule matches on type, channel, product and amount, product-specific beats any-product, channel-specific beats
any-channel, and after that the narrower amount range wins. With no matching rule, a withdrawal uses the product's own
`WithdrawalFee` and deposits and balance enquiries are free, so tenants that never touch the matrix see no change.
Internal deposits (loan disbursements, dividends) are never charged.

The fee is fixed on a `WithdrawalRequest` when it is made (amount, income GL account, segment, rule id), because the
fee is held along with the amount. A tariff change between request and payout therefore cannot change what the member
agreed to. Deposit fees post in the same journal as the deposit: the full amount is credited, then the fee is debited,
so the member's statement shows both.

### Maker-checker, and no in-place edits
Rules are created `PendingApproval` (`savings.fees.manage`) and go live only when a different user approves them
(`savings.fees.approve`, `MakerChecker.EnsureDistinct`). Changing a live rule means proposing a revision
(`supersedesRuleId`); approving the revision deactivates the rule it replaces. The audit trail therefore records
exactly which tariff applied and when. Approval is refused when an active rule already covers the same type, channel and
product over an overlapping amount range, so there is never a tie to break. Deactivation is a single step by a checker.

### Balance-enquiry fees are enforced by the server
A `BalanceEnquiry` rule is a fixed charge on a product. While it is active, self-service hides that product's balances:
`/api/self/accounts` returns null balances with `balanceLocked`, `/api/self/summary` nulls the affected bucket, and
`/api/self/statements/{account}` returns 422 `savings.balance.locked`. Running balances would otherwise give the balance
away, so Ledger asks Savings through the shared `IBalanceVisibility` contract and never reads its tables. A member pays
through `POST /api/self/accounts/{account}/balance-enquiries`, which debits their **FOSA** account (BOSA deposits and
share capital are not withdrawable, so they are never charged) and opens a reveal window
(`Savings:BalanceRevealMinutes`, default 5). The call is idempotent on an app-generated key, and it doesn't charge again
while a window is open. Staff views are never gated.

### "Require PIN to show balances" is a device preference
This protects against someone looking over the member's shoulder on a phone that is already signed in. It is not access
control, so the toggle is stored on the device and resets when the app goes to the background. The PIN itself is still
checked by the server: `POST /api/self/auth/pin/verify` returns `{ valid, lockedOut }` with 200, so a mistyped PIN never
trips the app's 401 handling. Failures share the sign-in lockout counter, so an unlocked phone can't be used to guess the
PIN either.

Members who turn the guard on can also choose to show balances with a fingerprint (or Face ID) instead of the PIN. This
check happens only on the device; the server never hears about it, and it only stands in for the PIN on this guard.
The prompt accepts biometrics only, not the phone's own screen-lock code, and the SACCO PIN always works as the
fallback. Changing either setting asks for the SACCO PIN, never the fingerprint: turning the guard off or adding the
fingerprint takes the PIN, and turning the fingerprint off needs nothing. Turning the guard off also clears the
fingerprint option. If the phone has no enrolled fingerprint any more, the app goes back to asking for the PIN.

## Consequences
- Every tenant gets a demoable tariff from the seed data: tiered M-Pesa and percentage Airtel/bank withdrawals, a
  check-off deposit fee, a KES 10 FOSA balance-enquiry fee, and one proposal waiting for approval.
- Members see the fee before committing: `GET /api/self/fees/quote` (mobile), `POST /api/savings/fees/quote` (portal
  calculator).
- Charges must be disclosed to members before they apply (consumer protection under the SACCO Societies Act and SASRA
  guidance). The quote endpoints make disclosure possible in the channels, but publishing a tariff guide is still an
  operational task for each SACCO. Confirm the disclosure rules with compliance before go-live.
- `WithdrawalRequest` rows made before this change have no stored fee GL account; payout falls back to the product's
  income GL account for them.
