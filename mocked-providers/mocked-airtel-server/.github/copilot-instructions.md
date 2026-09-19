# Mock Airtel Money Server Copilot Instructions

## Project Overview
This is a **mock implementation of Airtel Money API** built with .NET 10 ASP.NET Core Web API. It simulates Airtel Money payment endpoints for local development and testing without requiring actual Airtel Money credentials or internet connectivity.

## Purpose
- **Local Testing**: Test Airtel Money integrations offline without real API dependencies
- **Predictable Responses**: Control success/failure scenarios for automated testing
- **Faster Development**: No network latency or API rate limits
- **Cost Savings**: Avoid API charges during development

## Architecture

### Technology Stack
- **.NET 10** ASP.NET Core Web API
- **Minimal APIs** with controllers
- **HttpClient** for callback delivery
- **Dependency Injection** for services

### Project Structure
```
MockedAirtel.API/
├── Controllers/
│   ├── OAuthController.cs         # OAuth token generation (POST /auth/oauth2/token)
│   ├── CollectionController.cs    # Collection/Push (POST /merchant/v1/payments)
│   ├── DisbursementController.cs  # Disbursement (POST /standard/v1/disbursements)
│   ├── RefundController.cs        # Refund (POST /standard/v1/refund)
│   └── EnquiryController.cs       # Transaction/Balance enquiry (GET /standard/v1/payments/{id}, GET /standard/v1/balance)
├── Services/
│   └── CallbackService.cs         # Async callback delivery with delays
├── Models/
│   └── AirtelModels.cs            # Airtel Money request/response DTOs
└── Program.cs                     # Application configuration
```

## Implemented Endpoints

### 1. OAuth Token Endpoint
- **Route**: `POST /auth/oauth2/token`
- **Authentication**: Basic Auth (accepts any valid base64-encoded credentials)
- **Response**: Mock access token with Bearer type and 3600s expiry
- **Purpose**: Simulates Airtel Money OAuth authentication

### 2. Collection Endpoint (Money In)
- **Route**: `POST /merchant/v1/payments`
- **Authentication**: Bearer token (from OAuth endpoint)
- **Request**: `CollectionRequest` with reference, subscriber (msisdn), and transaction details
- **Response**: `CollectionResponse` with transaction status "TS" (Transaction Successful)
- **Callback**: Automatically sent to `X-Callback-Url` header after 2-5 second delay
- **Purpose**: Simulates customer payment collection (push payment)

### 3. Disbursement Endpoint (Money Out)
- **Route**: `POST /standard/v1/disbursements`
- **Authentication**: Bearer token
- **Request**: `DisbursementRequest` with payee (msisdn), reference, and transaction details
- **Response**: `DisbursementResponse` with transaction status "TS"
- **Callback**: Sent to `X-Callback-Url` header after 3-7 second delay
- **Purpose**: Simulates business-to-customer money transfers

### 4. Refund Endpoint
- **Route**: `POST /standard/v1/refund`
- **Authentication**: Bearer token
- **Request**: `RefundRequest` with airtel_money_id
- **Response**: `RefundResponse` with new transaction ID and status "TS"
- **Purpose**: Simulates transaction refunds

### 5. Transaction Enquiry
- **Route**: `GET /standard/v1/payments/{transactionId}`
- **Authentication**: Bearer token
- **Response**: `EnquiryResponse` with transaction details including airtel_money_id, amount, fees, status
- **Purpose**: Query status of any Airtel Money transaction

### 6. Balance Enquiry
- **Route**: `GET /standard/v1/balance`
- **Authentication**: Bearer token
- **Response**: `BalanceResponse` with available balance and currency
- **Purpose**: Query Airtel Money account balance

## Key Components

### CallbackService
- **Interface**: `ICallbackService`
- **Implementation**: Background HTTP callbacks to payment service
- **Features**:
  - Realistic delays (2-5s for collection, 3-7s for disbursement)
  - Automatic Airtel Money ID generation (format: `AM{yyyyMMddHHmmss}{random}`)
  - Detailed callback with transaction status
  - Error handling and logging

### Models
All models match exact structure from Airtel Money API:
- `TokenResponse` - OAuth token response
- `CollectionRequest/Response` - Collection flow with subscriber and transaction
- `DisbursementRequest/Response` - Disbursement with payee
- `RefundRequest/Response` - Refund transactions
- `EnquiryResponse` - Transaction enquiry details
- `BalanceResponse` - Account balance
- `CollectionCallback` - Callback payload

## Response Structure

All Airtel responses follow this pattern:
```json
{
  "data": { /* endpoint-specific data */ },
  "status": {
    "code": "200",
    "message": "SUCCESS",
    "result_code": "ESB000010",
    "response_code": "DP00800001006",
    "success": true
  }
}
```

## Status Codes
- **TS** - Transaction Successful
- **TF** - Transaction Failed
- **TA** - Transaction Ambiguous
- **TIP** - Transaction In Progress

## Configuration

### Port Configuration
- HTTPS: `https://localhost:5002`
- HTTP: `http://localhost:5001`

## Integration with Payment Service

### Database Configuration
Update payment service's `provider_credentials` table:
```sql
UPDATE provider_credentials
SET base_url = 'http://localhost:5001'
WHERE provider_type = 'AirtelMoney' AND environment = 'Sandbox';
```

### Testing Flow
1. Payment service calls `POST /auth/oauth2/token` to get token
2. Payment service calls collection/disbursement with token
3. Mock server returns immediate response with transaction ID
4. CallbackService triggers callback to payment service after delay
5. Payment service saga processes callback and updates state

## Development Guidelines

### Adding New Endpoints
1. Create model in `Models/AirtelModels.cs`
2. Create controller in `Controllers/`
3. Add validation and logging
4. Implement callback via `ICallbackService` if needed
5. Update README.md with endpoint documentation

### Callback Implementation
- Use `ICallbackService` methods for all callbacks
- Randomize delays to simulate real-world latency
- Generate realistic Airtel Money IDs (AM prefix + timestamp)
- Include all required fields (id, message, status_code, airtel_money_id)
- Log callback success/failure

### Custom Headers
Airtel Money uses `X-Callback-Url` header for callback URLs (not in request body)

## Current Limitations
- **No Real Validation**: Accepts any credentials for ease of testing
- **Always Succeeds**: All operations return success (configurable in future)
- **No Persistence**: In-memory state only
- **Fixed Balance**: Returns hardcoded balance of 500,000 KES

## Future Enhancements
- [ ] Configurable success/failure rates per endpoint
- [ ] Admin endpoints to control mock behavior
- [ ] Transaction state persistence (in-memory or Redis)
- [ ] Simulate Airtel timeout errors
- [ ] Support for Airtel error codes (insufficient balance, invalid phone, etc.)
- [ ] Request/response logging to file

## Related Projects
- The SACCO platform's Payments module (`backend/src/Sacco.Modules.Payments`): the real consumer of this mock API
- **MockedMpesa.API**: Similar mock server for M-Pesa
- Located at: `/Users/bett/Documents/Pochi-playground/payments/`

## Build and Run
```bash
cd /Users/bett/Documents/Pochi-playground/mocked-airtel-server/MockedAirtel.API
dotnet build    # Build project
dotnet run      # Start server
```

## Important Notes for Copilot
- This is a **mock/test server**, not production code
- Security is intentionally relaxed (accepts any auth for testing)
- All responses are synthetic and predictable
- Callbacks are triggered asynchronously via background tasks
- Models must match exact structure from real Airtel Money API for compatibility
- Airtel uses different route patterns than M-Pesa (merchant/v1 vs standard/v1)
