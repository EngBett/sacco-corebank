# mocked-equity-server

Mock of the **Equity Bank / Finserve "Jenga DFS" Partner REST APIs** for local development and
integration tests. Companion to `mocked-fingo`, `mocked-ncba`, `mocked-mpesa-server` and
`mocked-airtel-server`.

Spec source: [`Equity_Jenga_Docs.md`](../Equity_Jenga_Docs.md) (v1.0.8).

```bash
cd MockedEquity.API && dotnet run       # http://localhost:5104, Swagger at /
```

Port map across the mocks: mpesa `9998`, airtel `5028`, fingo `5102`, ncba `5103`, **equity `5104`**.

## What it reproduces deliberately

These are the behaviours that make the Equity integration different from every other provider we
have, so the mock keeps them rather than smoothing them over:

- **The acknowledgement carries no transaction id.** Payments answer
  `{"responseCode":"0","responseDesc":"Request accepted successfully","serviceStatus":"PENDING"}`
  and nothing else. The id only appears on the callback. This is why the payments service correlates
  on the `requestId` it generated.
- **Success and failure callbacks use different shapes.** Success is flat with
  `transactionReference`; failure nests under `metadata` and renames the echo to `requestReference`:

  ```jsonc
  // success
  {"transactionReference":"800800_TXN-1","resultType":"SUCCESS","resultCode":"00",
   "resultDesc":"Transaction processed successfully","transactionId":"XQ6390E542"}

  // failure — note metadata.requestReference, NOT transactionReference
  {"code":"3011","message":"Transaction failed","metadata":{"errorMessage":"Transaction failed",
   "requestReference":"800800_TXN-2","status":"FAILED","transactionId":"LD7704D918"}}
  ```

- **`requestId` must be `shortCode_uniqueRequestId`** — a request without the prefix is rejected
  with code `100`.
- **A reused `requestId` is rejected**, so retry paths have to be idempotent for real.
- **PINs and passwords must be AES-256-GCM encrypted** to spec: Base64 of
  `IV(12) || SALT(16) || CIPHERTEXT || TAG(16)`, key = PBKDF2-HMAC-SHA256(apiKey, salt, 65 536) →
  256 bits. The mock decrypts and compares; a wrong envelope gets code `101`. Set
  `Jenga:ValidateEncryptedCredentials=false` to skip that check.
- **Authentication is a separate host and header.** `POST /authentication/api/v3/authenticate/merchant`
  takes an `Api-Key` header (not a bearer token) and returns an **absolute** `expiresIn` timestamp,
  not a lifetime in seconds.

## Endpoints

| Method | Path | Notes |
|---|---|---|
| POST | `/authentication/api/v3/authenticate/merchant` | `Api-Key` header; returns bearer token |
| POST | `/momo-apis/api/v1/transaction/c2b/customer-initiated-payment` | collection (buy goods) |
| POST | `/momo-apis/api/v1/transaction/c2b/paybill` | collection (paybill) |
| POST | `/momo-apis/api/v1/transaction/b2c/business-to-customer-payment` | payout to wallet |
| POST | `/momo-apis/api/v1/transaction/b2b/buy-goods` | payout to till |
| POST | `/momo-apis/api/v1/transaction/b2b/pay-bill` | payout to biller |
| POST | `/momo-apis/api/v1/transaction/query-transaction-status` | by request id or transaction id |
| POST | `/momo-apis/api/v1/transaction/reverse-transaction` | reverses a `SUCCESS` transaction |
| POST | `/momo-apis/api/v1/transaction/organization/balance` | fixed float balance |

Everything under `/momo-apis` needs `Authorization: Bearer {token}`; without it you get a `401` with
code `101`, which is what drives the client's refresh-and-retry path.

## Forcing failures

Set `X-Simulate` on any payment request:

| Value | Behaviour |
|---|---|
| `fail` / `fail-async` | Accepted, then a **failure** callback |
| `reject` / `fail-ack` | Rejected at acknowledgement (`responseCode 103`, `serviceStatus FAILED`) |
| `no-callback` / `timeout` | Accepted, **no callback ever** — the stuck-saga case |
| `success` | Forces success regardless of amount |

Without the header, amount conventions apply (matching the other mocks): **13** → async failure,
**14** → acknowledgement rejection, **15** → no callback. Anything else succeeds.

## Test-only helpers (`/_test`, not on the real platform)

| Method | Path | Purpose |
|---|---|---|
| GET | `/_test/health` | liveness + transaction count |
| GET | `/_test/transactions` | everything the mock has seen |
| GET | `/_test/transactions/{id}` | one transaction, by request id or transaction id |
| POST | `/_test/expire-tokens` | expire all tokens to force a `401` on the next call |
| POST | `/_test/encrypt` | `{"value":"2580"}` → a correctly-enveloped payload |
| POST | `/_test/decrypt` | `{"encrypted":"…"}` → validates a client-produced payload |
| POST | `/_test/reset` | clear all transactions |

`/_test/decrypt` is the fastest way to diagnose "Equity keeps saying invalid credentials" — it tells
you whether your envelope is well-formed and whether the PIN matches.

## Configuration

`appsettings.json` → `Jenga` section: `ApiKey`, `MerchantCode`, `ConsumerSecret`, `ShortCode`,
`Pin`, `OrganizationUsername`, `OrganizationPassword`, `TokenLifetimeMinutes`,
`CallbackDelaySecondsMin/Max`, `ValidateEncryptedCredentials`.

The matching payments-side config lives under `ProviderCredentials:Equity` (see
`payments/src/Pochipay.Payments.API/appsettings.Local.json`) and already points here.

## Pointing payments at it

Routing is what selects Equity — the mock is only reachable once a rule targets it:

```bash
curl -X POST http://localhost:5292/api/admin/provider-routing \
  -H "Authorization: Bearer $ADMIN_TOKEN" -H "Content-Type: application/json" \
  -d '{"sourceProvider":"MPesa","credentialType":"C2B","riskLevel":"Low",
       "targetProvider":"EquityBank","priority":10,
       "description":"Low-risk M-Pesa collections via Equity"}'
```

The merchant still sees `MPesa` on the transaction; `ExecutedProvider` becomes `EquityBank` and is
visible only to the backoffice. See
[`docs/EQUITY_PROVIDER_INTEGRATION_PLAN.md`](../docs/EQUITY_PROVIDER_INTEGRATION_PLAN.md).

`./test-equity-endpoints.sh` runs the whole surface against a live instance.
