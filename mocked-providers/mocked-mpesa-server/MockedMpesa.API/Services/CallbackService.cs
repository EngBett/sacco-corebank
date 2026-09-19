using MockedMpesa.API.Models;
using MockedMpesa.API.Utilities;
using MockedMpesa.API.Data;
using System.Text;
using System.Text.Json;
using Refit;
using MongoDB.Driver;

namespace MockedMpesa.API.Services;

public interface ICallbackService
{
    Task SendStkPushCallbackAsync(string callbackUrl, string merchantRequestId, string checkoutRequestId, decimal amount, string phoneNumber, string accountReference, string shortCode);
    Task SendB2CCallbackAsync(string resultUrl, string conversationId, string originatorConversationId, string transactionId, decimal amount, string partyB);
    Task SendReversalCallbackAsync(string resultUrl, string conversationId, string originatorConversationId, string transactionId, decimal amount);
    Task SendTransactionStatusCallbackAsync(string resultUrl, string conversationId, string originatorConversationId, string transactionId, string originalConversationId);
    Task SendAccountBalanceCallbackAsync(string resultUrl, string conversationId, string originatorConversationId, string partyA);
}

public class CallbackService : ICallbackService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CallbackService> _logger;
    private readonly MockMpesaDbContext _dbContext;

    public CallbackService(
        IHttpClientFactory httpClientFactory, 
        ILogger<CallbackService> logger,
        MockMpesaDbContext dbContext)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task SendStkPushCallbackAsync(string callbackUrl, string merchantRequestId, string checkoutRequestId, decimal amount, string phoneNumber, string accountReference, string shortCode)
    {
        // Create transaction record
        var transaction = new TransactionRecord
        {
            MerchantRequestId = merchantRequestId,
            CheckoutRequestId = checkoutRequestId,
            TransactionType = "StkPush",
            PhoneNumber = phoneNumber,
            Amount = amount,
            AccountReference = accountReference,
            CallbackUrl = callbackUrl,
            ShortCode = shortCode,
            CreatedAt = DateTime.UtcNow
        };

        // Load simulation settings from database (must be synchronous to determine behavior)
        var settings = await _dbContext.SimulationSettings
            .Find(FilterDefinition<SimulationSettings>.Empty)
            .FirstOrDefaultAsync() 
            ?? new SimulationSettings(); // Fallback to defaults if not found

        // Save transaction to database asynchronously (fire-and-forget)
        _ = Task.Run(async () =>
        {
            try
            {
                await _dbContext.Transactions.InsertOneAsync(transaction);
                _logger.LogDebug("Transaction {MerchantRequestId} saved to database", merchantRequestId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save transaction {MerchantRequestId} to database", merchantRequestId);
            }
        });

        // Check if should skip callback
        if (transaction.SkipCallback || settings.GlobalSkipCallback)
        {
            // Update status asynchronously (fire-and-forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    var filter = Builders<TransactionRecord>.Filter.Eq(t => t.MerchantRequestId, merchantRequestId);
                    var update = Builders<TransactionRecord>.Update.Set(t => t.CallbackStatus, "Skipped");
                    await _dbContext.Transactions.UpdateOneAsync(filter, update);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update transaction status for {MerchantRequestId}", merchantRequestId);
                }
            });
            
            _logger.LogWarning("Skipping callback for transaction {MerchantRequestId}", merchantRequestId);
            return;
        }

        // Simulate processing delay
        var delay = Random.Shared.Next(settings.MinDelayMs, settings.MaxDelayMs);
        _logger.LogInformation("Delaying callback for {Delay}ms for transaction {MerchantRequestId}", delay, merchantRequestId);
        await Task.Delay(delay);

        // Determine if should simulate failure
        var shouldFail = transaction.SimulateFailure || settings.GlobalSimulateFailure;
        var resultCode = shouldFail ? settings.FailureResultCode : 0;
        var resultDesc = shouldFail ? settings.FailureResultDesc : "The service request is processed successfully.";
        
        var mpesaReceiptNumber = ReferenceGenerator.GenerateMpesaReceiptNumber();
        var transactionDate = DateTime.Now.ToString("yyyyMMddHHmmss");

        var callback = new StkPushCallbackRoot
        {
            Body = new StkPushCallbackBody
            {
                StkCallback = new StkPushCallback
                {
                    MerchantRequestId = merchantRequestId,
                    CheckoutRequestId = checkoutRequestId,
                    ResultCode = resultCode,
                    ResultDesc = resultDesc,
                    CallbackMetadata = resultCode == 0 ? new CallbackMetadata
                    {
                        Item =
                        [
                            new CallbackItem { Name = "Amount", Value = amount },
                            new CallbackItem { Name = "MpesaReceiptNumber", Value = mpesaReceiptNumber },
                            new CallbackItem { Name = "Balance", Value = null },
                            new CallbackItem { Name = "TransactionDate", Value = long.Parse(transactionDate) },
                            new CallbackItem { Name = "PhoneNumber", Value = long.Parse(phoneNumber) }
                        ]
                    } : null
                }
            }
        };

        try
        {
            var callbackApi = RestService.For<ICallbackApi>(callbackUrl);
            await callbackApi.SendStkCallback(callback);

            // Update transaction status asynchronously (fire-and-forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    var filter = Builders<TransactionRecord>.Filter.Eq(t => t.MerchantRequestId, merchantRequestId);
                    var update = Builders<TransactionRecord>.Update
                        .Set(t => t.CallbackSentAt, DateTime.UtcNow)
                        .Set(t => t.CallbackStatus, "Success");
                    if (!shouldFail)
                    {
                        update = update.Set(t => t.MpesaReceiptNumber, mpesaReceiptNumber);
                    }
                    await _dbContext.Transactions.UpdateOneAsync(filter, update);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update transaction status for {MerchantRequestId}", merchantRequestId);
                }
            });

            _logger.LogInformation("STK Push callback sent successfully for {MerchantRequestId}", merchantRequestId);
        }
        catch (Refit.ApiException ex)
        {
            _logger.LogWarning("Failed to send STK Push callback for {MerchantRequestId}, StatusCode:{StatusCode}, ErrorMessage : {ErrorMessage}", merchantRequestId, ex.StatusCode, ex.Content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send STK Push callback for {MerchantRequestId}", merchantRequestId);
        }
        
        // Send C2B Confirmation callback if registered
        if (!shouldFail)
        {
            // Fire-and-forget for C2B confirmation
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendC2BConfirmationAsync(shortCode, merchantRequestId, mpesaReceiptNumber, amount, phoneNumber, accountReference);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send C2B confirmation for {MerchantRequestId}", merchantRequestId);
                }
            });
        }
    }
    
    private async Task SendC2BConfirmationAsync(string shortCode, string merchantRequestId, string mpesaReceiptNumber, decimal amount, string phoneNumber, string accountReference)
    {
        try
        {
            // Get C2B registration for this specific short code
            var filter = Builders<C2BRegistration>.Filter.Eq(r => r.ShortCode, shortCode);
            var registration = await _dbContext.C2BRegistrations
                .Find(filter)
                .FirstOrDefaultAsync();
            
            if (registration?.ConfirmationUrl == null)
            {
                _logger.LogInformation("No C2B confirmation URL registered, skipping C2B confirmation for {MerchantRequestId}", merchantRequestId);
                return;
            }
            
            var transTime = DateTime.Now.ToString("yyyyMMddHHmmss");
            
            var c2bPayload = new C2BValidationRequest
            {
                TransactionType = "PayBill",
                TransID = mpesaReceiptNumber,
                TransTime = transTime,
                TransAmount = amount.ToString("F2"),
                BusinessShortCode = registration.ShortCode,
                BillRefNumber = accountReference,
                InvoiceNumber = "",
                OrgAccountBalance = "50000.00",
                ThirdPartyTransID = merchantRequestId,
                MSISDN = phoneNumber,
                FirstName = "John",
                MiddleName = "Doe",
                LastName = "Smith"
            };
            
            var httpClient = _httpClientFactory.CreateClient();
            var json = JsonSerializer.Serialize(c2bPayload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await httpClient.PostAsync(registration.ConfirmationUrl, content);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("C2B confirmation sent successfully for {MerchantRequestId} to {ConfirmationUrl}", 
                    merchantRequestId, registration.ConfirmationUrl);
            }
            else
            {
                _logger.LogWarning("Failed to send C2B confirmation for {MerchantRequestId}. Status: {StatusCode}", 
                    merchantRequestId, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending C2B confirmation for {MerchantRequestId}", merchantRequestId);
        }
    }

    public async Task SendB2CCallbackAsync(string resultUrl, string conversationId, string originatorConversationId, string transactionId, decimal amount, string partyB)
    {
        // Save B2C transaction record asynchronously (fire-and-forget)
        var record = new B2CTransactionRecord
        {
            ConversationId = conversationId,
            OriginatorConversationId = originatorConversationId,
            TransactionId = transactionId,
            Amount = amount,
            PartyB = partyB,
            ResultUrl = resultUrl,
            CreatedAt = DateTime.UtcNow
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await _dbContext.B2CTransactions.InsertOneAsync(record);
                _logger.LogDebug("B2C transaction record saved: {ConversationId}", conversationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save B2C transaction record: {ConversationId}", conversationId);
            }
        });

        // Simulate processing delay (3-7 seconds)
        await Task.Delay(Random.Shared.Next(3000, 7000));

        var transactionCompletedDateTime = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");

        var callback = new ResultCallbackRoot
        {
            Result = new ResultParameters
            {
                ResultType = 0,
                ResultCode = 0,
                ResultDesc = "The service request is processed successfully.",
                OriginatorConversationId = originatorConversationId,
                ConversationId = conversationId,
                TransactionId = transactionId,
                ReferenceData = new ResultParameterArray
                {
                    ResultParameter =
                    [
                        new() { Key = "TransactionAmount", Value = amount },
                        new() { Key = "TransactionReceipt", Value = transactionId },
                        new() { Key = "B2CRecipientIsRegisteredCustomer", Value = "Y" },
                        new() { Key = "B2CChargesPaidAccountAvailableFunds", Value = -4600.00 },
                        new() { Key = "ReceiverPartyPublicName", Value = $"{partyB} - John Doe" },
                        new() { Key = "TransactionCompletedDateTime", Value = transactionCompletedDateTime },
                        new() { Key = "B2CUtilityAccountAvailableFunds", Value = 10116.00 },
                        new() { Key = "B2CWorkingAccountAvailableFunds", Value = 900000.00 }
                    ]
                }
            }
        };

        var callbackApi = RestService.For<ICallbackApi>(resultUrl);
        await callbackApi.SendB2CCallback(callback);
        await SendCallbackAsync(resultUrl, callback, "B2C");

        // Update callback status asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                var filter = Builders<B2CTransactionRecord>.Filter.Eq(x => x.ConversationId, conversationId);
                var update = Builders<B2CTransactionRecord>.Update
                    .Set(x => x.CallbackSentAt, DateTime.UtcNow)
                    .Set(x => x.CallbackStatus, "Success");
                await _dbContext.B2CTransactions.UpdateOneAsync(filter, update);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update B2C callback status: {ConversationId}", conversationId);
            }
        });
    }

    public async Task SendReversalCallbackAsync(string resultUrl, string conversationId, string originatorConversationId, string transactionId, decimal amount)
    {
        // Simulate processing delay (3-7 seconds)
        await Task.Delay(Random.Shared.Next(3000, 7000));

        var callback = new ResultCallbackRoot
        {
            Result = new ResultParameters
            {
                ResultType = 0,
                ResultCode = 0,
                ResultDesc = "The service request is processed successfully.",
                OriginatorConversationId = originatorConversationId,
                ConversationId = conversationId,
                TransactionId = transactionId,
                ReferenceData = new ResultParameterArray
                {
                    ResultParameter = new List<ResultParameterItem>
                    {
                        new() { Key = "TransactionID", Value = transactionId },
                        new() { Key = "ReversedAmount", Value = amount },
                        new() { Key = "CompletedTime", Value = DateTime.Now.ToString("yyyyMMddHHmmss") }
                    }
                }
            }
        };

        await SendCallbackAsync(resultUrl, callback, "Reversal");
    }

    public async Task SendTransactionStatusCallbackAsync(string resultUrl, string conversationId, string originatorConversationId, string transactionId, string originalConversationId)
    {
        // Simulate processing delay (3-7 seconds)
        await Task.Delay(Random.Shared.Next(3000, 7000));

        // Look up the transaction in B2C or STK collections
        B2CTransactionRecord? b2cRecord = null;
        TransactionRecord? stkRecord = null;

        if (!string.IsNullOrEmpty(transactionId))
        {
            b2cRecord = await _dbContext.B2CTransactions
                .Find(Builders<B2CTransactionRecord>.Filter.Eq(x => x.TransactionId, transactionId))
                .FirstOrDefaultAsync();

            if (b2cRecord == null)
            {
                stkRecord = await _dbContext.Transactions
                    .Find(Builders<TransactionRecord>.Filter.Eq(x => x.TransactionId, transactionId))
                    .FirstOrDefaultAsync();
            }
        }

        // Fall back to OriginalConversationID lookup if TransactionID not found
        if (b2cRecord == null && stkRecord == null && !string.IsNullOrEmpty(originalConversationId))
        {
            b2cRecord = await _dbContext.B2CTransactions
                .Find(Builders<B2CTransactionRecord>.Filter.Eq(x => x.OriginatorConversationId, originalConversationId))
                .FirstOrDefaultAsync();
        }

        // Build callback parameters from real transaction data where available
        decimal txAmount = b2cRecord?.Amount ?? stkRecord?.Amount ?? 0;
        string receiptNo = b2cRecord?.TransactionId ?? stkRecord?.MpesaReceiptNumber ?? stkRecord?.TransactionId ?? transactionId;
        string creditPartyName = b2cRecord != null
            ? $"{b2cRecord.PartyB} - Customer"
            : stkRecord != null
                ? $"{stkRecord.PhoneNumber} - Customer"
                : "Unknown - Customer";
        string debitPartyName = stkRecord?.ShortCode != null
            ? $"{stkRecord.ShortCode} - Merchant"
            : "600000 - Test Company";
        string resolvedConversationId = b2cRecord?.ConversationId ?? stkRecord?.MerchantRequestId ?? conversationId;
        string resolvedOriginatorConversationId = b2cRecord?.OriginatorConversationId ?? originatorConversationId;

        var callback = new ResultCallbackRoot
        {
            Result = new ResultParameters
            {
                ResultType = 0,
                ResultCode = 0,
                ResultDesc = "The service request is processed successfully.",
                OriginatorConversationId = resolvedOriginatorConversationId,
                ConversationId = resolvedConversationId,
                TransactionId = receiptNo,
                ReferenceData = new ResultParameterArray
                {
                    ResultParameter = new List<ResultParameterItem>
                    {
                        new() { Key = "ReceiptNo", Value = receiptNo },
                        new() { Key = "ConversationID", Value = resolvedConversationId },
                        new() { Key = "FinalisedTime", Value = DateTime.Now.ToString("yyyyMMddHHmmss") },
                        new() { Key = "Amount", Value = txAmount },
                        new() { Key = "TransactionStatus", Value = "Completed" },
                        new() { Key = "ReasonType", Value = b2cRecord != null ? "Business Payment to Customer via API" : "Pay Bill" },
                        new() { Key = "TransactionReason", Value = "Payment processed successfully" },
                        new() { Key = "DebitPartyCharges", Value = "" },
                        new() { Key = "DebitAccountType", Value = "Utility Account" },
                        new() { Key = "InitiatedTime", Value = (b2cRecord?.CreatedAt ?? stkRecord?.CreatedAt ?? DateTime.Now.AddMinutes(-1)).ToString("yyyyMMddHHmmss") },
                        new() { Key = "OriginatorConversationID", Value = resolvedOriginatorConversationId },
                        new() { Key = "CreditPartyName", Value = creditPartyName },
                        new() { Key = "DebitPartyName", Value = debitPartyName }
                    }
                }
            }
        };

        await SendCallbackAsync(resultUrl, callback, "Transaction Status");
    }

    public async Task SendAccountBalanceCallbackAsync(string resultUrl, string conversationId, string originatorConversationId, string partyA)
    {
        // Simulate processing delay (3-7 seconds)
        await Task.Delay(Random.Shared.Next(3000, 7000));

        var callback = new ResultCallbackRoot
        {
            Result = new ResultParameters
            {
                ResultType = 0,
                ResultCode = 0,
                ResultDesc = "The service request is processed successfully.",
                OriginatorConversationId = originatorConversationId,
                ConversationId = conversationId,
                TransactionId = $"QGR{Random.Shared.Next(10000000, 99999999)}",
                ReferenceData = new ResultParameterArray
                {
                    ResultParameter = new List<ResultParameterItem>
                    {
                        new() { Key = "AccountBalance", Value = "Working Account|KES|500000.00|500000.00|0.00|0.00" },
                        new() { Key = "BOCompletedTime", Value = DateTime.Now.ToString("yyyyMMddHHmmss") }
                    }
                }
            }
        };

        await SendCallbackAsync(resultUrl, callback, "Account Balance");
    }

    private async Task SendCallbackAsync(string url, object payload, string operationType)
    {
        if (string.IsNullOrEmpty(url))
        {
            _logger.LogWarning("{OperationType} callback URL is empty, skipping callback", operationType);
            return;
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("Sending {OperationType} callback to {Url}", operationType, url);

            var response = await httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("{OperationType} callback sent successfully to {Url}", operationType, url);
            }
            else
            {
                _logger.LogWarning("{OperationType} callback failed with status {StatusCode} to {Url}", operationType, response.StatusCode, url);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send {OperationType} callback to {Url}", operationType, url);
        }
    }
}
