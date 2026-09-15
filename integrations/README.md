# Provider integration guides

Vendor documentation kept alongside the code that implements it. Nothing here is built.

| File | Provider | Implemented by |
|---|---|---|
| `Safaricom APIs.postman_collection.json` | M-Pesa Daraja (STK push, B2C, C2B, reversal, status) | `backend/src/Sacco.Modules.Payments/Providers/DarajaMpesaProvider.cs` |
| `DFS Jenga Integration API Documentation v1.0.8.md` | Equity / Finserve Jenga DFS Partner REST APIs | `…/Providers/BankProviders.cs` (`EquityJengaProvider`) |
| `sample-ncbagateway apis.txt` | NCBA Payments API (sample client) | `…/Providers/BankProviders.cs` (`NcbaProvider`) |

The Airtel Money reference implementation lives under `../reference/Brij.AirtelMoney`; the mock gateways for all four
providers under `../mocked-providers`.
