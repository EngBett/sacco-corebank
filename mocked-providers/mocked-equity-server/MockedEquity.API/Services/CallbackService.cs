using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MockedEquity.API.Models;

namespace MockedEquity.API.Services;

public interface ICallbackService
{
    /// <summary>
    /// Schedules the asynchronous result Equity would post after processing. Fire-and-forget, like
    /// the real gateway — the caller has already been acknowledged with serviceStatus PENDING.
    /// </summary>
    void ScheduleResult(EquityTransaction transaction);
}

public class CallbackService : ICallbackService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITransactionStore _store;
    private readonly JengaOptions _options;
    private readonly ILogger<CallbackService> _logger;

    public CallbackService(
        IHttpClientFactory httpClientFactory,
        ITransactionStore store,
        IOptions<JengaOptions> options,
        ILogger<CallbackService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    public void ScheduleResult(EquityTransaction transaction)
    {
        if (string.IsNullOrWhiteSpace(transaction.CallbackUrl))
        {
            _logger.LogWarning(
                "No callbackUrl on {RequestId}; the transaction will stay PENDING until queried.",
                transaction.RequestId);
            return;
        }

        _ = Task.Run(() => DeliverAsync(transaction));
    }

    private async Task DeliverAsync(EquityTransaction transaction)
    {
        try
        {
            var delay = Random.Shared.Next(
                Math.Max(0, _options.CallbackDelaySecondsMin),
                Math.Max(1, _options.CallbackDelaySecondsMax) + 1);

            await Task.Delay(TimeSpan.FromSeconds(delay));

            // Resolve the outcome only now: a test may have flipped the transaction to FAILED via
            // the control endpoints while the callback was pending.
            var current = _store.Find(transaction.RequestId) ?? transaction;
            var succeeded = !string.Equals(current.Status, "FAILED", StringComparison.OrdinalIgnoreCase);

            current.Status = succeeded ? "SUCCESS" : "FAILED";
            _store.Update(current);

            object payload = succeeded
                ? new SuccessCallback
                {
                    TransactionReference = current.RequestId,
                    ResultType = "SUCCESS",
                    ResultCode = "00",
                    ResultDesc = "Transaction processed successfully",
                    TransactionId = current.TransactionId
                }
                : new FailureCallback
                {
                    Code = current.FailureCode ?? "3011",
                    Message = current.FailureReason ?? "Transaction failed",
                    Metadata = new FailureCallbackMetadata
                    {
                        ErrorMessage = current.FailureReason ?? "Transaction failed",
                        RequestReference = current.RequestId,
                        Status = "FAILED",
                        TransactionId = current.TransactionId
                    }
                };

            var json = JsonSerializer.Serialize(payload, payload.GetType(), SerializerOptions);

            _logger.LogInformation(
                "Posting Equity {Outcome} callback for {RequestId} to {CallbackUrl} after {Delay}s: {Json}",
                succeeded ? "SUCCESS" : "FAILURE", current.RequestId, current.CallbackUrl, delay, json);

            using var client = _httpClientFactory.CreateClient("callbacks");
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(current.CallbackUrl, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Callback for {RequestId} acknowledged with {StatusCode}",
                    current.RequestId, (int)response.StatusCode);
            }
            else
            {
                // The real gateway retries here. The mock does not — a stuck saga is exactly the
                // condition the recovery workers are supposed to resolve, so leave it stuck.
                _logger.LogWarning(
                    "Callback for {RequestId} rejected with {StatusCode}; not retrying.",
                    current.RequestId, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver callback for {RequestId}", transaction.RequestId);
        }
    }
}
