# Mock provider servers

Stand-ins for the real payment gateways and the SMS channel, used to exercise the platform's **live** provider
classes end to end without credentials. Each is an independent ASP.NET app with its own README; none is part of
`backend/Sacco.sln`.

## Payment providers (profile `mocks`, opt-in)

| Mock | Stands in for | Platform provider | Port (host) | Notes |
|---|---|---|---|---|
| `mocked-mpesa-server` | Safaricom Daraja | `DarajaMpesaProvider` (`MPesa`) | 9998 | Needs MongoDB + RabbitMQ (queued callbacks); the compose profile runs them as sidecars. STK query is at `stkpushquery/v2/query` (set `StkQueryPath`). |
| `mocked-airtel-server` | Airtel Money Open API | `AirtelMoneyProvider` (`AirtelMoney`) | 5028 | Basic-auth token, `reference` as `{id}`, disbursements at `standard/v1/disbursements`, callback via `X-Callback-Url`. |
| `mocked-equity-server` | Equity / Finserve Jenga DFS | `EquityJengaProvider` (`Bank:EQUITY`) | 5104 | Validates the AES-GCM PIN envelope; amounts 13/14/15 or `X-Simulate` force failures. |
| `mocked-ncba` | NCBA Payments API | `NcbaProvider` (`Bank:NCBA`) | 5103 | Synchronous Pesalink transfer; `X-Simulate-Error-Code` forces an error. |

## Notification channels (default, always on with `postgres`)

| Mock | Stands in for | Platform sender | Port (host) | Notes |
|---|---|---|---|---|
| [Mailpit](https://github.com/axllent/mailpit) (`axllent/mailpit` image, not vendored here) | Any SMTP provider | `SmtpEmailSender` | 8025 (web UI), 1025 (SMTP) | No config needed — `SmtpEmailSender` already speaks plain SMTP, so pointing it at Mailpit needs no code, just `Notifications:Email:Smtp:Host=mailpit\|localhost`. |
| `mocked-sms-server` | Any SMS gateway (ADR 0012) | `MockSmsSender` (`Notifications:Sms:Provider=Mock`) | 5108 (web UI + API) | Generic `POST /sms/send` → `{messageId,status}`; `GET /api/messages` lists what was "sent"; a `to` ending `0000` or header `X-Simulate-Error: true` forces a failure. |

These two start by default with `docker compose up -d postgres mailpit mocked-sms` — see `backend/CLAUDE.md` and ADR 0012.
`appsettings.Development.json` already points a locally run API (`dotnet run --project backend/src/Sacco.Api`) at both.

## Run them

```bash
docker compose --profile mocks up -d --build          # all four (M-Pesa with its sidecars)
# or individually, from each folder:  Vault__VaultUri='#' dotnet run
```

`Vault__VaultUri=#` disables the Vault configuration source the Airtel and M-Pesa mocks otherwise expect.

## Point the platform at them (Live mode, no credentials)

```bash
cd backend
ASPNETCORE_ENVIRONMENT=Development IdentityServer__IssuerUri=http://localhost:5010 \
Payments__MPesa__Mode=Live Payments__MPesa__BaseUrl=http://localhost:9998/ \
Payments__MPesa__ConsumerKey=b2a9eb8e-9c1c-4a0d-9f1e-5b2c3d4e5f6g Payments__MPesa__ConsumerSecret=c3d4e5f6g7h8i9j0k1l2m3n4o5p6q7r8s \
Payments__MPesa__ShortCode=174379 Payments__MPesa__Passkey=mock-passkey Payments__MPesa__InitiatorName=testapi Payments__MPesa__SecurityCredential=mock-credential Payments__MPesa__StkQueryPath=mpesa/stkpushquery/v2/query \
Payments__MPesa__CallbackBaseUrl=http://host.docker.internal:5010/api/payments/webhooks/demo \
Payments__AirtelMoney__Mode=Live Payments__AirtelMoney__BaseUrl=http://localhost:5028/ Payments__AirtelMoney__ClientId=mock-client Payments__AirtelMoney__ClientSecret=mock-secret \
Payments__AirtelMoney__AirtelReferenceAsObject=true Payments__AirtelMoney__AirtelDisbursementPath=standard/v1/disbursements \
Payments__AirtelMoney__CallbackBaseUrl=http://localhost:5010/api/payments/webhooks/demo Payments__AirtelMoney__CallbackToken=airtel-cb \
Payments__Banks__EQUITY__Mode=Live Payments__Banks__EQUITY__BaseUrl=http://localhost:5104/ Payments__Banks__EQUITY__ApiKey=local-api-key \
Payments__Banks__EQUITY__MerchantCode=0582910862 Payments__Banks__EQUITY__ConsumerSecret=local-consumer-secret Payments__Banks__EQUITY__ShortCode=800800 Payments__Banks__EQUITY__Pin=2580 \
Payments__Banks__EQUITY__CallbackBaseUrl=http://localhost:5010/api/payments/webhooks/demo Payments__Banks__EQUITY__CallbackToken=equity-cb \
Payments__Banks__NCBA__Mode=Live Payments__Banks__NCBA__BaseUrl=http://localhost:5103/ Payments__Banks__NCBA__ApiKey=ke123 Payments__Banks__NCBA__ApiUser=sacco \
Payments__Banks__NCBA__DefaultBankCode=07 Payments__Banks__NCBA__DefaultBranchCode=000 Payments__DefaultBankProvider=Bank:NCBA \
dotnet run --project src/Sacco.Api --no-launch-profile --urls http://localhost:5010
```

The M-Pesa consumer key, secret and bearer token above are the placeholders given to the mock in `docker-compose.yml`.
Callbacks from mocks running in Docker reach the API through `host.docker.internal`; mocks started with `dotnet run` can use `localhost`.

## End-to-end check

`e2e/run-local.sh` drives an M-Pesa STK push and B2C payout, an Airtel USSD push, an Equity C2B collection and an NCBA Pesalink payout through the
API above and waits for each to settle (callback or synchronous receipt), printing the receipt and the ledger journal.
It is the pre-UAT rehearsal for each provider: when it passes, the only remaining unknowns are the real credentials
and any drift between the mock and the live gateway.
