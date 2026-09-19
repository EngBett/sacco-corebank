using MockedAirtel.API.Models;
using System.Text;
using System.Text.Json;

namespace MockedAirtel.API.Services;

public interface ICallbackService
{
    Task SendCollectionCallbackAsync(string callbackUrl, string transactionId, string airtelMoneyId, string message, string statusCode);
    Task SendDisbursementCallbackAsync(string callbackUrl, string transactionId, string airtelMoneyId, string message, string statusCode);
}

public class CallbackService : ICallbackService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CallbackService> _logger;

    public CallbackService(IHttpClientFactory httpClientFactory, ILogger<CallbackService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SendCollectionCallbackAsync(string callbackUrl, string transactionId, string airtelMoneyId, string message, string statusCode)
    {
        // Simulate processing delay (2-5 seconds)
        await Task.Delay(Random.Shared.Next(2000, 5000));

        var callback = new CollectionCallback
        {
            transaction = new TransactionCallback
            {
                id = transactionId,
                message = message,
                status_code = statusCode,
                airtel_money_id = airtelMoneyId
            }
        };

        await SendCallbackAsync(callbackUrl, callback, "Collection");
    }

    public async Task SendDisbursementCallbackAsync(string callbackUrl, string transactionId, string airtelMoneyId, string message, string statusCode)
    {
        // Simulate processing delay (3-7 seconds)
        await Task.Delay(Random.Shared.Next(3000, 7000));

        var callback = new CollectionCallback
        {
            transaction = new TransactionCallback
            {
                id = transactionId,
                message = message,
                status_code = statusCode,
                airtel_money_id = airtelMoneyId
            }
        };

        await SendCallbackAsync(callbackUrl, callback, "Disbursement");
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
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
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
