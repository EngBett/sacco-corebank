# Mock M-Pesa Server

A mock implementation of Safaricom's M-Pesa Daraja API for local development and testing.

## Overview

This mock server simulates M-Pesa Daraja API endpoints, allowing you to test payment integrations locally without requiring actual M-Pesa credentials or internet connectivity.

## Features

- **OAuth Token Generation** - Mock authentication endpoint
- **STK Push** - Simulate customer payment prompts
- **B2C Payments** - Simulate business-to-customer disbursements
- **Reversals** - Simulate transaction reversals
- **Realistic Callbacks** - Automatic callbacks with configurable delays
- **Configurable Responses** - Control success rates and response times

## Endpoints

### 1. OAuth Token
```http
GET /oauth/v1/generate?grant_type=client_credentials
Authorization: Basic {base64(consumer_key:consumer_secret)}
```

**Note**: The mock server validates credentials against configured ConsumerKey and ConsumerSecret.
Use: `b2a9eb8e-9c1c-4a0d-9f1e-5b2c3d4e5f6g:c3d4e5f6g7h8i9j0k1l2m3n4o5p6q7r8s`

Response:
```json
{
  "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJtb2NrZWRfcG9jaGlwYXkiLCJhdWQiOiJtb2NrZWRfcG9jaGlwYXkiLCJpc3MiOiJtb2NrZWRfcG9jaGlwYXkiLCJleHAiOjE2",
  "expires_in": "3599"
}
```

### 2. STK Push
```http
POST /mpesa/stkpush/v1/processrequest
Authorization: Bearer {access_token}
Content-Type: application/json

{
  "BusinessShortCode": "174379",
  "Password": "...",
  "Timestamp": "20240115120000",
  "TransactionType": "CustomerPayBillOnline",
  "Amount": 100,
  "PartyA": "254712345678",
  "PartyB": "174379",
  "PhoneNumber": "254712345678",
  "CallBackURL": "https://your-domain.com/callback",
  "AccountReference": "ORDER123",
  "TransactionDesc": "Payment for order"
}
```

Response:
```json
{
  "MerchantRequestID": "MR-20240115120000-abc123",
  "CheckoutRequestID": "ws_CO_20240115120000456",
  "ResponseCode": "0",
  "ResponseDescription": "Success. Request accepted for processing",
  "CustomerMessage": "Success. Request accepted for processing"
}
```

**Callback** (sent to `CallBackURL` after 2-5 seconds):
```json
{
  "Body": {
    "stkCallback": {
      "MerchantRequestID": "MR-20240115120000-abc123",
      "CheckoutRequestID": "ws_CO_20240115120000456",
      "ResultCode": 0,
      "ResultDesc": "The service request is processed successfully.",
      "CallbackMetadata": {
        "Item": [
          { "Name": "Amount", "Value": 100 },
          { "Name": "MpesaReceiptNumber", "Value": "QGR12345678" },
          { "Name": "TransactionDate", "Value": 20240115120530 },
          { "Name": "PhoneNumber", "Value": 254712345678 }
        ]
      }
    }
  }
}
```

### 3. B2C Payment
```http
POST /mpesa/b2c/v1/paymentrequest
Authorization: Bearer {access_token}
Content-Type: application/json

{
  "InitiatorName": "testapi",
  "SecurityCredential": "...",
  "CommandID": "BusinessPayment",
  "Amount": 500,
  "PartyA": "600000",
  "PartyB": "254712345678",
  "Remarks": "Salary Payment",
  "QueueTimeOutURL": "https://your-domain.com/timeout",
  "ResultURL": "https://your-domain.com/result",
  "Occasion": "Monthly"
}
```

Response:
```json
{
  "ConversationID": "AG_20240115120000_abc123def456",
  "OriginatorConversationID": "uuid-here",
  "ResponseCode": "0",
  "ResponseDescription": "Accept the service request successfully."
}
```

### 4. Reversal
```http
POST /mpesa/reversal/v1/request
Authorization: Bearer {access_token}
Content-Type: application/json

{
  "Initiator": "testapi",
  "SecurityCredential": "...",
  "CommandID": "TransactionReversal",
  "TransactionID": "QGR12345678",
  "Amount": 100,
  "ReceiverParty": "600000",
  "RecieverIdentifierType": "11",
  "Remarks": "Reversal",
  "QueueTimeOutURL": "https://your-domain.com/timeout",
  "ResultURL": "https://your-domain.com/result",
  "Occasion": ""
}
```

## C2B URL Registration and Confirmation

### Overview

The Mock M-Pesa server supports C2B (Customer to Business) URL registration, allowing shortcodes to receive dual callbacks for STK Push transactions:
1. **Standard STK Push Callback** - Sent to the callback URL specified in the STK Push request
2. **C2B Confirmation Callback** - Sent to the ConfirmationURL from the C2B registration

### Registration Flow

```
1. Client registers shortcode with confirmation URL
   POST /api/admin/c2b/register
   
2. Registration saved to database (C2BRegistrations table)

3. STK Push initiated with registered shortcode
   POST /mpesa/stkpush/v1/processrequest

4. Mock M-Pesa checks if shortcode is registered
   - If yes: Send BOTH callbacks
   - If no: Send only STK Push callback

5. Callbacks sent after configurable delay:
   - STK Push Callback → CallbackURL (from request)
   - C2B Confirmation → ConfirmationURL (from registration)
```

### API Endpoints

#### Register C2B URLs

**Endpoint**: `POST /api/admin/c2b/register`

**Request Body**:
```json
{
  "ShortCode": "600966",
  "ValidationURL": "http://localhost:5003/api/payments/c2b/validation",
  "ConfirmationURL": "http://localhost:5003/api/payments/c2b/confirmation",
  "ResponseType": "Completed"
}
```

**Response** (201 Created):
```json
{
  "id": 1,
  "shortCode": "600966",
  "validationUrl": "http://localhost:5003/api/payments/c2b/validation",
  "confirmationUrl": "http://localhost:5003/api/payments/c2b/confirmation",
  "responseType": "Completed",
  "registeredAt": "2024-01-23T10:30:00Z"
}
```

#### Get All C2B Registrations

**Endpoint**: `GET /api/admin/c2b/registrations`

**Response** (200 OK):
```json
[
  {
    "id": 1,
    "shortCode": "600966",
    "validationUrl": "http://...",
    "confirmationUrl": "http://...",
    "responseType": "Completed",
    "registeredAt": "2024-01-23T10:30:00Z",
    "updatedAt": null
  }
]
```

#### Delete C2B Registration

**Endpoint**: `DELETE /api/admin/c2b/registrations/{id}`

**Response** (204 No Content)

### C2B Confirmation Payload

When an STK Push transaction uses a registered shortcode, a C2B confirmation is sent:

```json
{
  "TransactionType": "PayBill",
  "TransID": "RKL51ZDR4F",
  "TransTime": "20231121121325",
  "TransAmount": "100.00",
  "BusinessShortCode": "600966",
  "BillRefNumber": "XYZ123AB-aB3x7Km9",
  "MSISDN": "254712345678",
  "FirstName": "John",
  "MiddleName": "Doe",
  "LastName": "Smith"
}
```

### Database Schema

**Table**: `C2BRegistrations`

| Column | Type | Description |
|--------|------|-------------|
| Id | BIGINT | Primary key |
| ShortCode | VARCHAR(20) | Registered shortcode (unique) |
| ValidationUrl | VARCHAR(500) | Optional validation endpoint |
| ConfirmationUrl | VARCHAR(500) | Required confirmation endpoint |
| ResponseType | VARCHAR(20) | "Completed" or "Cancelled" |
| RegisteredAt | TIMESTAMP | Registration timestamp |
| UpdatedAt | TIMESTAMP | Last update timestamp |

**Indexes**:
- Unique index on `ShortCode` (prevents duplicate registrations)

### Testing

**Step 1**: Register a shortcode
```bash
curl -X POST http://localhost:5005/api/admin/c2b/register \
  -H "Content-Type: application/json" \
  -d '{
    "ShortCode": "600966",
    "ConfirmationURL": "http://localhost:5003/api/payments/c2b/confirmation",
    "ResponseType": "Completed"
  }'
```

**Step 2**: Initiate STK Push with registered shortcode
```bash
POST http://localhost:5005/mpesa/stkpush/v1/processrequest
{
  "BusinessShortCode": "600966",
  "Amount": 100,
  "PhoneNumber": "254712345678",
  "AccountReference": "TEST123",
  "TransactionDesc": "Payment"
}
```

**Step 3**: Verify dual callbacks received:
- Check STK Push callback URL logs
- Check C2B confirmation URL logs

## Simulation Features

### Overview

The Mock M-Pesa server includes comprehensive simulation features for testing failure scenarios, missed callbacks, and edge cases without modifying code.

### Database Persistence

**SQLite Database**: `mockedmpesa.db`

**TransactionRecord Table**:
| Column | Type | Description |
|--------|------|-------------|
| Id | BIGINT | Primary key |
| MerchantRequestId | VARCHAR(50) | Unique request ID |
| CheckoutRequestId | VARCHAR(50) | Unique checkout ID |
| TransactionId | VARCHAR(50) | M-Pesa transaction ID |
| TransactionType | VARCHAR(20) | StkPush, B2C, Reversal |
| Amount | DECIMAL | Transaction amount |
| PhoneNumber | VARCHAR(20) | Customer phone |
| CallbackUrl | VARCHAR(500) | Callback endpoint |
| MpesaReceiptNumber | VARCHAR(50) | M-Pesa receipt |
| CreatedAt | TIMESTAMP | Transaction created |
| CallbackSentAt | TIMESTAMP | Callback sent time |
| CallbackStatus | VARCHAR(20) | Success, Failed, Skipped |
| SimulateFailure | BOOLEAN | Per-transaction failure flag |
| SkipCallback | BOOLEAN | Per-transaction skip flag |

### Global Simulation Settings

**Configuration** (`appsettings.json`):
```json
{
  "SimulationSettings": {
    "GlobalSimulateFailure": false,  // All callbacks return failure
    "GlobalSkipCallback": false,     // All callbacks skipped (missed)
    "MinDelayMs": 2000,              // Min callback delay
    "MaxDelayMs": 5000,              // Max callback delay
    "FailureResultCode": 1,          // M-Pesa error code
    "FailureResultDesc": "Insufficient funds in the account"
  }
}
```

### Simulation Logic

**Callback Decision Tree**:
```
1. Check transaction.SkipCallback || GlobalSkipCallback
   → Yes: Skip callback, set CallbackStatus = "Skipped"
   
2. Check transaction.SimulateFailure || GlobalSimulateFailure
   → Yes: Send failure callback (ResultCode = 1)
   → No: Send success callback (ResultCode = 0)

3. Random delay: Random.Shared.Next(MinDelayMs, MaxDelayMs)

4. Send HTTP POST to callback URL

5. Update CallbackStatus based on HTTP response
```

### Admin API Endpoints

#### Get All Transactions

**Endpoint**: `GET /api/admin/transactions?pageNumber=1&pageSize=100`

**Response**:
```json
{
  "transactions": [
    {
      "id": 1,
      "merchantRequestId": "REQ123",
      "transactionId": "RKL51ZDR4F",
      "amount": 100.00,
      "phoneNumber": "254712345678",
      "callbackStatus": "Success",
      "simulateFailure": false,
      "skipCallback": false,
      "createdAt": "2024-01-23T10:00:00Z"
    }
  ],
  "totalCount": 250,
  "pageNumber": 1,
  "pageSize": 100
}
```

#### Get Transaction by ID

**Endpoint**: `GET /api/admin/transactions/{id}`

**Response** (200 OK): Single transaction object

#### Toggle Simulate Failure

**Endpoint**: `PUT /api/admin/transactions/{id}/simulate-failure`

**Request Body**:
```json
{
  "simulateFailure": true
}
```

**Response** (200 OK): Updated transaction

#### Toggle Skip Callback

**Endpoint**: `PUT /api/admin/transactions/{id}/skip-callback`

**Request Body**:
```json
{
  "skipCallback": true
}
```

**Response** (200 OK): Updated transaction

#### Retry Callback

**Endpoint**: `POST /api/admin/transactions/{id}/retry-callback`

**Purpose**: Manually retry missed/failed callbacks

**Response** (200 OK):
```json
{
  "message": "Callback retry successful",
  "callbackStatus": "Success"
}
```

#### Get Simulation Settings

**Endpoint**: `GET /api/admin/settings`

**Response** (200 OK):
```json
{
  "globalSimulateFailure": false,
  "globalSkipCallback": false,
  "minDelayMs": 2000,
  "maxDelayMs": 5000,
  "failureResultCode": 1,
  "failureResultDesc": "Insufficient funds"
}
```

#### Get Statistics

**Endpoint**: `GET /api/admin/statistics`

**Response** (200 OK):
```json
{
  "total": 1000,
  "success": 950,
  "failed": 30,
  "skipped": 20,
  "pending": 0
}
```

### Testing Scenarios

**Scenario 1: Failed Collection (Insufficient Funds)**
```json
// Set global failure
{
  "GlobalSimulateFailure": true
}

// Initiate STK Push → Callback returns ResultCode = 1
// Expected: Collection saga → HandleProviderCallFailureActivity → Failed state
```

**Scenario 2: Missed Callback (Network Issue)**
```json
// Set global skip
{
  "GlobalSkipCallback": true
}

// Initiate STK Push → No callback sent, CallbackStatus = "Skipped"
// Expected: Collection saga stuck in AwaitingCallback state
// Admin can retry: POST /api/admin/transactions/{id}/retry-callback
```

**Scenario 3: Per-Transaction Failure**
```bash
# Normal flow enabled globally
# Initiate STK Push → Get transaction ID from database
PUT /api/admin/transactions/5/simulate-failure (true)
# Retry callback → Returns failure despite global success setting
```

## Transactions UI

### Overview

The Mock M-Pesa server includes a **Transactions page** that displays all transactions in a user-friendly table format.

### Accessing the Page

**URL**: `http://localhost:9998/transactions`

**Purpose**: View all STK Push and payment transactions with real-time status updates

### Features

**Table View**:
- All transactions ordered by most recent
- Color-coded status badges:
  - 🟢 **Success** (Green) - Transaction completed successfully
  - 🔴 **Failed** (Red) - Transaction failed
  - 🟡 **Skipped** (Yellow) - Callback was skipped
  - 🔵 **Pending** (Blue) - Transaction pending callback

**Transaction Details**:
- ID, Transaction Type
- Merchant Request ID, Checkout Request ID
- Phone Number, Amount
- M-Pesa Receipt Number
- Callback Status
- Created At, Callback Sent timestamps

**Actions**:
- **Refresh Button**: Reload data from database
- **Navigation**: Quick links to Settings page
- **Auto-refresh**: Optional (every 30 seconds)

### Usage

1. **Start Mock M-Pesa**:
   ```bash
   cd mocked-mpesa-server/MockedMpesa.API
   dotnet run
   ```

2. **Access Page**: `http://localhost:9998/transactions`

3. **Test Flow**:
   - Send STK Push request
   - Wait for callback processing
   - Click "Refresh" to see new transactions
   - Check status badges for callback results

4. **Configure Simulation**: Navigate to Settings page to toggle simulation options

### Technical Details

- **Framework**: Blazor Server (ASP.NET Core 10.0)
- **Database**: MongoDB
- **Styling**: Bootstrap 5 with Bootstrap Icons
- **Rendering**: Interactive Server mode
- **Data Loading**: Async with loading spinner

### Empty State

If no transactions exist, displays:
> "No transactions found. Initiate an STK Push to see transactions here."

## C2B Registrations UI

### Overview

The C2B Registrations page displays and manages C2B confirmation registrations for M-Pesa shortcodes.

### Accessing the Page

**URL**: `http://localhost:5005/c2b-registrations`

**Purpose**: View and manage shortcode confirmation URLs for dual callback functionality

### Features

**Table View**:

| Column | Description |
|--------|-------------|
| ID | Database ID of the registration |
| ShortCode | M-Pesa shortcode (e.g., "600966") |
| Validation URL | Endpoint for transaction validation (optional) |
| Confirmation URL | Endpoint for confirmation callbacks |
| Response Type | "Completed" or "Cancelled" |
| Registered At | When the shortcode was registered |
| Updated At | Last update timestamp |
| Actions | Delete button for each registration |

**Actions**:
- **Refresh Button**: Reload registrations from database
- **Delete Button**: Remove registration with confirmation dialog
- **Navigation**: Quick links to Transactions and Settings pages

### How C2B Registrations Work

**Dual Callback System**:

When an STK Push transaction uses a registered shortcode, **two callbacks** are sent:

1. **STK Push Callback** (Standard M-Pesa):
   - Sent to CallbackURL from STK Push request
   - Contains standard transaction details
   - Uses `SendStkPushCallbackAsync()` method

2. **C2B Confirmation Callback** (Enhanced Feature):
   - Sent to ConfirmationURL from C2B registration
   - Contains C2B-formatted transaction details
   - Uses `SendC2BConfirmationAsync()` method

### Registration Guide

**Step 1**: Register shortcode via Admin API
```bash
POST http://localhost:5005/api/admin/c2b/register
{
  "ShortCode": "600966",
  "ValidationURL": "http://localhost:5003/api/payments/c2b/validation",
  "ConfirmationURL": "http://localhost:5003/api/payments/c2b/confirmation",
  "ResponseType": "Completed"
}
```

**Step 2**: View in UI
1. Navigate to `http://localhost:5005/c2b-registrations`
2. See the registered shortcode in the table
3. Verify the confirmation URL is correct

**Step 3**: Test with STK Push
```bash
POST http://localhost:5005/api/stkpush/initiate
{
  "BusinessShortCode": "600966",
  "Amount": 100,
  "PhoneNumber": "254712345678",
  "AccountReference": "TEST123"
}
```

**Step 4**: Verify dual callbacks received in both endpoints

### Technical Details

**Database Table**: `C2BRegistrations`
```csharp
public class C2BRegistration
{
    public long Id { get; set; }
    public string ShortCode { get; set; }           // Required, unique
    public string? ValidationUrl { get; set; }       // Optional
    public string? ConfirmationUrl { get; set; }     // Required for confirmations
    public string ResponseType { get; set; }         // "Completed" or "Cancelled"
    public DateTime RegisteredAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
```

**UI Styling**:
- **Theme**: Dark mode (`data-bs-theme="dark"`)
- **Bootstrap 5** components
- **Primary color** for header (blue) to distinguish from other pages
- **Responsive table** with hover and striping effects

### Navigation

All pages in Mock M-Pesa UI:
- **Transactions** → `/transactions`
- **C2B Registrations** → `/c2b-registrations`
- **Settings** → `/settings`

```bash
cd MockedMpesa.API
dotnet run
```

The server will start on `https://localhost:5001` (HTTPS) and `http://localhost:5000` (HTTP).

## Configuration

Edit `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "MongoDB": "mongodb://localhost:27017"
  },
  "MongoDB": {
    "DatabaseName": "MockedMpesaDb"
  },
  "Credentials": {
    "ConsumerKey": "b2a9eb8e-9c1c-4a0d-9f1e-5b2c3d4e5f6g",
    "ConsumerSecret": "c3d4e5f6g7h8i9j0k1l2m3n4o5p6q7r8s",
    "BearerToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJtb2NrZWRfcG9jaGlwYXkiLCJhdWQiOiJtb2NrZWRfcG9jaGlwYXkiLCJpc3MiOiJtb2NrZWRfcG9jaGlwYXkiLCJleHAiOjE2"
  },
  "MockMpesa": {
    "CallbackDelay": {
      "MinSeconds": 2,
      "MaxSeconds": 5
    },
    "SuccessRate": 0.95,
    "EnableDetailedLogging": true
  }
}
### Authentication

The mock server validates credentials to simulate real M-Pesa authentication:

1. **OAuth Endpoint**: Validates Basic Auth credentials (ConsumerKey:ConsumerSecret) and returns configured BearerToken
2. **Protected Endpoints**: All payment endpoints validate the Bearer token against the configured value
3. **Error Handling**: Returns 401 Unauthorized if credentials or token are invalid

## Testing with Payment Service

Update your payment service's M-Pesa credentials in the database to point to the mock server:

```sql
UPDATE provider_credentials
SET base_url = 'http://localhost:5000'
WHERE provider_type = 'MPesa'
  AND environment = 'Sandbox';
```

Or in `appsettings.Development.json` of your payment service:

```json
{
  "Mpesa": {
    "BaseUrl": "http://localhost:5000",
    "ConsumerKey": "b2a9eb8e-9c1c-4a0d-9f1e-5b2c3d4e5f6g",
    "ConsumerSecret": "c3d4e5f6g7h8i9j0k1l2m3n4o5p6q7r8s"
  }
}
```
```

## Development Notes

- **No Real Validation**: The mock server accepts any credentials for ease of testing
- **Always Succeeds**: By default, all operations return success (configurable via `SuccessRate`)
- **Callbacks**: Automatically sent after a configurable delay (2-5 seconds default)
- **Logging**: Detailed logging is enabled in Development mode

## Architecture

```
MockedMpesa.API/
├── Controllers/
│   ├── OAuthController.cs      # Token generation
│   ├── StkPushController.cs    # STK Push endpoint
│   ├── B2CController.cs        # B2C payments
│   └── ReversalController.cs   # Reversals
├── Services/
│   └── CallbackService.cs      # Handles async callbacks
└── Models/
    └── MpesaModels.cs          # Request/Response models
```

## License

MIT
