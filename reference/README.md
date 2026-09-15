# Reference material

Vendored integrations kept for reference only. Nothing here is part of `backend/Sacco.sln`, CI, or any
Docker image; the platform never depends on these projects at build or run time.

| Folder | What it is | What was taken from it |
|---|---|---|
| `Brij.AirtelMoney/` | A working Airtel Money Open API integration (Refit client, token provider, USSD push, B2C disbursement with RSA-encrypted PIN, callback handling). | The request/response shapes, headers, endpoint paths and status codes behind `backend/src/Sacco.Modules.Payments/Providers/AirtelMoneyProvider.cs`. Its dependencies (MassTransit, Hangfire, Refit) were deliberately not brought across (ADR 0003). |

Do not add secrets here. Anything credential-shaped in these folders is a placeholder.
