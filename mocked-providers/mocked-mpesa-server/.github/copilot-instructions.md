# Mock M-Pesa Server Copilot Instructions

## Project Overview
This is a **mock implementation of Safaricom's M-Pesa Daraja API** built with .NET 10 ASP.NET Core Web API. It simulates M-Pesa payment endpoints for local development and testing without requiring actual M-Pesa credentials or internet connectivity.

## Purpose
- **Local Testing**: Test payment integrations offline without real API dependencies
- **Predictable Responses**: Control success/failure scenarios for automated testing
- **Faster Development**: No network latency or API rate limits
- **Cost Savings**: Avoid API charges during development

## Architecture

### Technology Stack
- **.NET 10** ASP.NET Core Web API
- **Minimal APIs** with controllers
- **MongoDB** for data persistence
- **HttpClient** for callback delivery
- **Dependency Injection** for services

### Project Structure
```
MockedMpesa.API/
├── Attributes/
│   └── ValidateBearerTokenAttribute.cs  # Bearer token validation filter
├── Configuration/
│   ├── MpesaCredentialsOptions.cs       # Credentials configuration
│   └── VaultOptions.cs                  # Vault configuration
├── Controllers/
│   ├── OAuthController.cs      # OAuth token generation (GET /oauth/v1/generate)
│   ├── StkPushController.cs    # STK Push simulation (POST /mpesa/stkpush/v1/processrequest)
│   ├── B2CController.cs        # B2C disbursements (POST /mpesa/b2c/v1/paymentrequest)
│   └── ReversalController.cs   # Transaction reversals (POST /mpesa/reversal/v1/request)
├── Data/
│   └── MockMpesaDbContext.cs   # MongoDB database context
├── Services/
│   └── CallbackService.cs      # Async callback delivery with delays
├── Models/
│   └── MpesaModels.cs          # M-Pesa request/response DTOs
└── Program.cs                  # Application configuration with MongoDB setup
```

## Authentication & Authorization

### OAuth Flow
1. **Get Token**: Client sends Basic Auth (ConsumerKey:ConsumerSecret) to `/oauth/v1/generate`
2. **Validate Credentials**: Server validates against configured credentials
3. **Return BearerToken**: Server returns configured BearerToken from appsettings
4. **Use Token**: Client uses BearerToken in `Authorization: Bearer <token>` header for all API requests

### Bearer Token Validation
- All protected endpoints (STK Push, B2C, Reversal, etc.) use `[ValidateBearerToken]` attribute
- Validates incoming Bearer token against configured `BearerToken` in appsettings
- Returns 401 Unauthorized if token is missing, malformed, or doesn't match
- Implemented as an `IAuthorizationFilter` for clean separation of concerns

## Implemented Endpoints

### 1. OAuth Token Endpoint
- **Route**: `GET /oauth/v1/generate`
- **Authentication**: Basic Auth (validates against configured ConsumerKey:ConsumerSecret)
- **Response**: Configured Bearer token with 3599s expiry
- **Purpose**: Simulates M-Pesa OAuth authentication
- **Note**: Returns 401 if credentials don't match configured values

### 2. STK Push Endpoint
- **Route**: `POST /mpesa/stkpush/v1/processrequest`
- **Authentication**: Bearer token (validated against configured BearerToken)
- **Request**: `StkPushRequest` with amount, phone number, callback URL, etc.
- **Response**: `StkPushResponse` with MerchantRequestID and CheckoutRequestID
- **Callback**: Automatically sent to `CallBackURL` after 2-5 second delay
- **Purpose**: Simulates customer-initiated payment prompts

### 3. B2C Payment Endpoint
- **Route**: `POST /mpesa/b2c/v1/paymentrequest`
- **Authentication**: Bearer token (validated against configured BearerToken)
- **Request**: `B2CRequest` with amount, recipient phone, result URL, etc.
- **Response**: `B2CResponse` with ConversationID and OriginatorConversationID
- **Callback**: Sent to `ResultURL` after 3-7 second delay
- **Purpose**: Simulates business-to-customer disbursements

### 4. Reversal Endpoint
- **Route**: `POST /mpesa/reversal/v1/request`
- **Authentication**: Bearer token (validated against configured BearerToken)
- **Request**: `ReversalRequest` with transaction ID, amount, result URL, etc.
- **Response**: `ReversalResponse` with ConversationID
- **Callback**: Sent to `ResultURL` after 3-7 second delay
- **Purpose**: Simulates payment reversals

## Key Components

### CallbackService
- **Interface**: `ICallbackService`
- **Implementation**: Background HTTP callbacks to payment service
- **Features**:
  - Realistic delays (2-5s for STK, 3-7s for B2C/Reversal)
  - Automatic M-Pesa receipt number generation
  - Detailed callback metadata matching real API structure
  - Error handling and logging

### Models
All models use MongoDB attributes for data persistence:
- Models decorated with `[BsonId]` and `[BsonRepresentation(BsonType.ObjectId)]` for IDs
- `TransactionRecord` - Transaction records with string-based MongoDB ObjectId
- `SimulationSettings` - Configuration for mock behavior
- `C2BRegistration` - C2B URL registrations
- Request/Response models matching exact M-Pesa Daraja API structure

## Configuration

### appsettings.json
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
```

### Port Configuration
MongoDB connection string should be configured in appsettings:
```json
{
  "ConnectionStrings": {
    "MongoDB": "mongodb://localhost:27017"
  },
  "MongoDB": {
    "DatabaseName": "MockedMpesaDb"
  }
}
```

Update payment service to use mock M-Pesa server
- HTTP: `http://localhost:5000`

## Integration with Payment Service

### Database Configuration with MongoDB attributes
2. Create controller in `Controllers/`
3. Add validation and logging
4. Implement callback via `ICallbackService`
5. Ensure MongoDB operations use proper filters and updates
6. Update README.md with endpoint documentation

### Working with MongoDB
- Use `Builders<T>.Filter` for query filters
- Use `Builders<T>.Update` for update operations
- All IDs are MongoDB ObjectIds (string representation)
- Indexes are created automatically in `MockMpesaDbContext`
- Use `Find()`, `InsertOneAsync()`, `UpdateOneAsync()`, `DeleteManyAsync()`

### Callback Implementation
- Use `ICallbackService` methods for all callbacks
- Randomize delays to simulate real-world latency
- Generate realistic M-Pesa receipt numbers
- Include all required metadata fields
- Log callback success/failure
- Update transaction status in MongoDB after callbackte response with request IDs
4. CallbackService triggers callback to payment service after delay
5. Payment service saga processes callback and updates state

## Development Guidelines

### Adding New Endpoints
1. Create model in `Models/MpesaModels.cs`
2. Create controller in `Controllers/`
3. Add validation and logging
4. Implement callback via `ICallbackService`
5. Update README.md with endpoint documentation

### Callback Implementation
- Use `ICallbackService` methods for all callbacks
- Randomize delays to simulate real-world latency
- Generate realistic M-Pesa receipt numbers
- Include all required metadata fields
- Log callback success/failure

### Error Simulation
To simulate failures (future enhancement):
- Check `SuccessRate` configuration
- Return appropriate M-Pesa error codes (e.g., ResultCode = 1032 for cancellation)
- Trigger failure callbacks for testing error handling

## Testing

### Manual Testing
```bash
# 1. Start mock server
cd MockedMpesa.API
dotnet run

# 2. Get token
curl -X GET "http://localhost:5000/oauth/v1/generate" \
  -H "Authorization: Basic dGVzdDp0ZXN0"

# 3. Trigger STK Push
curl -X POST "http://localhost:5000/mpesa/stkpush/v1/processrequest" \
  -H "Authorization: Bearer mock_token" \
  -H "CClustering**: MongoDB runs in standalone mode for local testing
- **No Admin Endpoints**: Cannot control behavior at runtime yet

## Future Enhancements
- [ ] Configurable success/failure rates per endpoint
- [ ] Admin endpoints to control mock behavior
- [ ] MongoDB replica set support for production scenarios
- [ ] Simulate M-Pesa timeout errors
- [ ] Support for M-Pesa error codes (insufficient funds, invalid phone, etc.)
- [ ] Webhook replay functionality
- [ ] Request/response logging to MongoDB collection
### Integration Testing
1. Start mock M-Pesa server on port 5000
2. Start payment service on port 8080
3. Configure payment service to use `http://localhost:5000`
4. Send payment request to payment service
5. Verify callback received and saga completed

## Current Limitations
- **No Real Validation**: Accepts any credentials for ease of testing
- **Always Succeeds**: All operations return success (configurable in future)
- **No Persistence**: In-memory state only
- **No Admin Endpoints**: Cannot control behavior at runtime yet

## Future Enhancements
- [ ] Configurable success/failure rates per endpoint
- [ ] Admin endpoints to control mock behavior
- [ ] Transaction state persistence (in-memory or Redis)
- [ ] Simulate M-Pesa timeout errors
- [ ] Support for M-Pesa error codes (insufficient funds, invalid phone, etc.)
- [ ] Webhook replay functionality
- **Database**: Uses MongoDB with ObjectId-based IDs (string type)
- **No Migrations**: MongoDB is schema-less, no migration files needed
- **Indexes**: Created automatically on startup in `MockMpesaDbContext`
- [ ] Request/response logging to file

## Related Projects
- The SACCO platform's Payments module (`backend/src/Sacco.Modules.Payments`): the real consumer of this mock API
- Located at: `/Users/bett/Documents/Pochi-playground/payments/`

## Build and Run
```bash
cd /Users/bett/Documents/Pochi-playground/mocked-mpesa-server/MockedMpesa.API
dotnet build    # Build project
dotnet run      # Start server
```

Build status: ✅ **Build succeeded** (verified)

## Important Notes for Copilot
- This is a **mock/test server**, not production code
- Security is intentionally relaxed (accepts any auth for testing)
- All responses are synthetic and predictable
- Callbacks are triggered asynchronously via background tasks
- Models must match exact structure from real M-Pesa API for compatibility
