# Mock Airtel Money Server

A mock implementation of Airtel Money API for local development and testing.

## Overview

This mock server simulates Airtel Money API endpoints, allowing you to test payment integrations locally without requiring actual Airtel Money credentials or internet connectivity.

## Features

- **OAuth Token Generation** - Mock authentication endpoint
- **Collection (Push)** - Simulate customer payment collection
- **Disbursement** - Simulate money transfers to customers
- **Refunds** - Simulate transaction refunds
- **Transaction Enquiry** - Query transaction status
- **Balance Enquiry** - Query account balance
- **Realistic Callbacks** - Automatic callbacks with configurable delays

## Endpoints

### 1. OAuth Token
```http
POST /auth/oauth2/token
Authorization: Basic {base64(client_id:client_secret)}
```

Response:
```json
{
  "access_token": "mock_token_xyz",
  "token_type": "Bearer",
  "expires_in": 3600
}
```

### 2. Collection (Money In)
```http
POST /merchant/v1/payments
Authorization: Bearer {access_token}
X-Callback-Url: https://your-domain.com/callback
Content-Type: application/json

{
  "reference": {
    "id": "ORDER123"
  },
  "subscriber": {
    "country": "KE",
    "currency": "KES",
    "msisdn": "254712345678"
  },
  "transaction": {
    "amount": 100,
    "country": "KE",
    "currency": "KES",
    "id": "TXN123"
  }
}
```

Response:
```json
{
  "data": {
    "transaction": {
      "id": "TXN123",
      "status": "TS"
    }
  },
  "status": {
    "code": "200",
    "message": "SUCCESS",
    "result_code": "ESB000010",
    "response_code": "DP00800001006",
    "success": true
  }
}
```

**Callback** (sent to `X-Callback-Url` after 2-5 seconds):
```json
{
  "transaction": {
    "id": "TXN123",
    "message": "Transaction processed successfully",
    "status_code": "TS",
    "airtel_money_id": "AM202412311200001234"
  }
}
```

### 3. Disbursement (Money Out)
```http
POST /standard/v1/disbursements
Authorization: Bearer {access_token}
X-Callback-Url: https://your-domain.com/callback
Content-Type: application/json

{
  "payee": {
    "msisdn": "254712345678"
  },
  "reference": {
    "id": "DISB123"
  },
  "transaction": {
    "amount": 500,
    "country": "KE",
    "currency": "KES",
    "id": "TXN456"
  }
}
```

### 4. Refund
```http
POST /standard/v1/refund
Authorization: Bearer {access_token}
Content-Type: application/json

{
  "transaction": {
    "airtel_money_id": "AM202412311200001234",
    "country": "KE",
    "currency": "KES"
  }
}
```

### 5. Transaction Enquiry
```http
GET /standard/v1/payments/{transactionId}
Authorization: Bearer {access_token}
```

Response:
```json
{
  "data": {
    "transaction": {
      "airtel_money_fee": 10.00,
      "airtel_money_id": "AM202412311200001234",
      "amount": 1000.00,
      "currency": "KES",
      "id": "TXN123",
      "message": "Transaction processed successfully",
      "status": "TS"
    }
  },
  "status": {
    "code": "200",
    "message": "SUCCESS"
  }
}
```

### 6. Balance Enquiry
```http
GET /standard/v1/balance
Authorization: Bearer {access_token}
```

Response:
```json
{
  "data": {
    "available_balance": "500000.00",
    "currency": "KES"
  },
  "status": {
    "code": "200",
    "message": "SUCCESS"
  }
}
```

## Running the Server

```bash
cd MockedAirtel.API
dotnet run
```

The server will start on `https://localhost:5002` (HTTPS) and `http://localhost:5001` (HTTP).

## Configuration

Edit `appsettings.Development.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "MockedAirtel.API": "Debug"
    }
  }
}
```

## Testing with Payment Service

Update your payment service's Airtel Money configuration to point to the mock server:

```sql
UPDATE provider_credentials
SET base_url = 'http://localhost:5001'
WHERE provider_type = 'AirtelMoney'
  AND environment = 'Sandbox';
```

## Status Codes

- **TS** - Transaction Successful
- **TF** - Transaction Failed
- **TA** - Transaction Ambiguous
- **TIP** - Transaction In Progress

## Architecture

```
MockedAirtel.API/
├── Controllers/
│   ├── OAuthController.cs         # Token generation
│   ├── CollectionController.cs    # Collection endpoint
│   ├── DisbursementController.cs  # Disbursement endpoint
│   ├── RefundController.cs        # Refund endpoint
│   └── EnquiryController.cs       # Transaction & balance enquiry
├── Services/
│   └── CallbackService.cs         # Handles async callbacks
└── Models/
    └── AirtelModels.cs            # Request/Response models
```

## License

MIT
