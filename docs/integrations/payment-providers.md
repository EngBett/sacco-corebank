# Payment provider integration

## Shared contract

Every provider (M-Pesa, Airtel Money, bank transfer/API) implements the same interface —
domain code never calls a provider SDK directly.

```csharp
public interface IPaymentProvider
{
    string ProviderName { get; } // "MPesa", "AirtelMoney", "Bank:<BankCode>"

    Task<CollectionResult> InitiateCollectionAsync(CollectionRequest request, CancellationToken ct);
    // STK push / USSD push — member pays into the SACCO (deposit, loan repayment)

    Task<DisbursementResult> InitiateDisbursementAsync(DisbursementRequest request, CancellationToken ct);
    // B2C — SACCO pays out to member (withdrawal, loan disbursement)

    Task<TransactionStatus> VerifyTransactionAsync(string providerReference, CancellationToken ct);
    // Reconciliation — poll provider directly, used when a callback never arrives

    Task<WebhookHandlingResult> HandleWebhookAsync(WebhookPayload payload, CancellationToken ct);
    // Idempotent — see below
}
```

## Idempotency (mandatory)

Every webhook handler must be safe to invoke more than once with the same payload. Enforce
this with a unique constraint on `(ProviderName, ProviderTransactionReference)` in a
`ProcessedProviderTransactions` table, checked/inserted atomically before any ledger posting.
A duplicate delivery must be detected and short-circuited, never silently re-posted.

## Sandbox-first

Every provider ships a sandbox/mock implementation, registered by default in local, CI, and
non-production environments. Provider selection and credentials come from configuration
(`appsettings` + secrets manager), keyed by environment and tenant — never a code-level
`if (isDemo)` branch. Going live for a given SACCO is: obtain their production credentials,
set the configuration, done. See `docs/runbooks/production-cutover.md`.

## Provider specifics to implement against (verify exact current API details against each
provider's live developer documentation before implementation — do not assume these are
unchanged from prior knowledge)

### M-Pesa (Safaricom)
- STK Push (Lipa na M-Pesa Online) for member-initiated collections
- C2B for paybill/till-based collections
- B2C for disbursements (loan payout, withdrawal)
- Callback URLs must be idempotency-checked per above; confirm current OAuth/credential flow
  against Safaricom Daraja documentation at implementation time

### Airtel Money
- Collection (USSD push) API for member-initiated deposits/repayments
- Disbursement API for payouts
- Confirm current API version and auth flow against Airtel Money developer documentation at
  implementation time — this integration is less commonly documented than M-Pesa's and
  deserves extra verification time

### Bank APIs
- Likely candidates: direct bank API/RTGS/EFT integration for a SACCO's settlement bank, or
  a bank-agnostic aggregator, depending on which partner bank(s) the pilot SACCO uses.
  Confirm the specific bank/aggregator before building — this is the most SACCO-specific of
  the three integrations and may differ per client.
- Model as a `Bank:<BankCode>` provider so multiple banks can be supported without changing
  the shared contract.

## Implementation (Phase 5)

- Contract: `Sacco.Shared.Payments.IPaymentProvider`; providers: `SandboxMpesaProvider`, `SandboxAirtelMoneyProvider`,
  `SandboxBankProvider` (default everywhere), `DarajaMpesaProvider` (live M-Pesa; selected by `Payments:MPesa:Mode=Live`).
  `AirtelMoneyProvider` (live Airtel Money; `Payments:AirtelMoney:Mode=Live`), written from the shape of a working
  integration kept under `reference/Brij.AirtelMoney` (OAuth2 client credentials, USSD push `merchant/v1/payments/`,
  enquiry `standard/v1/payments/{id}`, B2C `standard/v2/disbursements/` with the PIN RSA-encrypted under Airtel's public key,
  `X-Country`/`X-Currency` headers, national MSISDNs, snake_case JSON, `status_code` TS/TF/TIP callbacks). Verify against the
  Airtel UAT sandbox (`https://openapiuat.airtel.africa/`) before go-live. Configuration: `ClientId`, `ClientSecret`, `Country`,
  `Currency`, `DisbursementPin`, `DisbursementPublicKey`, `CallbackToken` (the callback URL registered with Airtel is
  `…/api/payments/webhooks/{tenant}/airtelmoney?token=<CallbackToken>`; TIP callbacks are answered 400 so Airtel retries with the
  final status).
- Bank providers are keyed by code under `Payments:Banks:{CODE}` and selected as `Bank:{CODE}`; `Payments:DefaultBankProvider`
  names the one used for bank-transfer withdrawals. Payout destinations are `BANKCODE|ACCOUNT|BENEFICIARY NAME|BRANCH`
  (later parts optional) or a bare account/phone number.
  - `EquityJengaProvider` (`Bank:EQUITY`, Finserve Jenga DFS Partner REST APIs v1.0.8 — `integrations/DFS Jenga…md`): merchant
    token via `Api-Key` header with an absolute expiry, C2B buy-goods collections, B2C payouts, PIN sent AES-256-GCM encrypted
    under the API key (`IV‖SALT‖CIPHER‖TAG`, PBKDF2-HMAC-SHA256 × 65 536), request ids `shortCode_reference`, status query by
    request id, success/failure callbacks with different shapes. Settings: `AuthBaseUrl`, `ApiKey`, `MerchantCode`,
    `ConsumerSecret`, `ShortCode`, `Pin`, `CallbackBaseUrl`, `CallbackToken`.
  - `NcbaProvider` (`Bank:NCBA`, NCBA Payments API — `integrations/sample-ncbagateway apis.txt`): a single synchronous transfer
    call (`API-Key`/`API-User` headers) for Pesalink, Internal, EFT, RTGS and Mwallet; the receipt comes back in the response,
    so the saga finalises immediately (`DisbursementResult.Completed`). References are 12 characters derived from ours.
    Payout only. Settings: `ApiKey`, `ApiUser`, `DefaultBankCode`, `DefaultBranchCode`.
- Daraja request fields are Pascal-case (`BusinessShortCode`, `CallBackURL`) and the provider serialises them as such; responses
  and callbacks are read case-insensitively because Daraja's own casing is inconsistent (`errorMessage` vs `ResponseCode`).
- Every live provider is exercised end to end against the mock gateways under `mocked-providers/` (see its README and
  `mocked-providers/e2e/run-local.sh`): USSD/STK push → callback → ledger posting, and synchronous bank payouts.
- Idempotency: `payments.processed_provider_transactions` has a unique index on (provider, provider transaction reference);
  `PaymentFinalizer` inserts there *before* any ledger posting, so a replayed webhook is a detected no-op.
- Callback URL per SACCO: `POST /api/payments/webhooks/{tenantSlug}/{mpesa|airtelmoney|bank:CODE}`. The tenant is resolved from
  the path; the endpoint is anonymous and rate-limited; provider authenticity checks belong in the provider class.
- Sagas: `CollectionSaga` / `DisbursementSaga` (Wolverine, Postgres-backed, schema `wolverine`). Initiate → PendingCallback →
  callback finalises; a `TimeoutMessage` after `Payments:CallbackTimeoutMinutes` calls `VerifyTransactionAsync` and either
  finalises or marks the transaction `TimedOut` for `POST /api/payments/transactions/{id}/reconcile`.
- Sandbox rules: counterparty ending `97` is rejected at initiation, `98` fails on callback/verification, anything else succeeds.
  Deliver the callback with `POST /api/payments/sandbox/transactions/{id}/callback[?success=false]`, or set
  `Payments:SandboxAutoCallbackSeconds` (Development default 5) to have it arrive on its own.
- Unsolicited credits (C2B paybill) whose account reference matches a savings account or loan are applied directly; unmatched
  ones are recorded and parked in FOSA suspense (2500) so the settlement account still reconciles.

## Sagas

Collection and disbursement flows that wait on an external callback are Wolverine sagas, not
synchronous calls. Each saga must define explicit timeout handling and a reconciliation path
(call `VerifyTransactionAsync` if no callback arrives within the expected window) — a payment
flow must never be left in permanent limbo.
