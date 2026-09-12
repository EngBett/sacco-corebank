---
name: payment-provider-integration
description: Use whenever adding, modifying, or debugging an M-Pesa, Airtel Money, or bank API integration, or any Wolverine saga that orchestrates a payment flow. Encodes the shared IPaymentProvider contract, the sandbox-first requirement, and the idempotency rule for webhook handling.
---

# Payment provider integration conventions

Full contract lives in `docs/integrations/payment-providers.md` — read it before writing
provider code. Summary of the rules that must never be broken:

## One shared interface
All providers (M-Pesa, Airtel Money, bank) implement the same `IPaymentProvider` contract:
`InitiateCollectionAsync`, `InitiateDisbursementAsync`, `VerifyTransactionAsync`, and a
webhook handler contract. Domain code (Lending, Savings) calls the interface, never a
provider SDK directly.

## Sandbox by default
Every provider ships with a sandbox/mock implementation registered by default in local and
CI environments. Selecting a real provider is a configuration change (base URL, credentials,
webhook signing secret) via environment/tenant config — never a code branch like
`if (isDemo)`.

## Idempotency is mandatory, not optional
Webhooks from all three provider types can and will be delivered more than once. Every
webhook handler must be safe to run twice: enforce a unique constraint on the provider's own
transaction reference, or check an idempotency-key table, before posting to the ledger.
A duplicate delivery must be a detectable no-op, verifiable by an integration test that
fires the same webhook payload twice.

## Long-running flows are sagas
STK push / USSD push collections and B2C disbursements involve waiting on an external
callback. Model these as Wolverine sagas with explicit timeout and reconciliation handling
(what happens if the callback never arrives?) — not as a synchronous call with a long
timeout bolted on.

## Every provider needs seed fixtures
Successful collection, failed/timed-out collection, duplicate webhook delivery, and
successful disbursement — for each provider. See `seed-data-generation`.
