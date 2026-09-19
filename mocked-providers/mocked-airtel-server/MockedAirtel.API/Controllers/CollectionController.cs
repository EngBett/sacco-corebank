using Microsoft.AspNetCore.Mvc;
using MockedAirtel.API.Models;
using MockedAirtel.API.Services;

namespace MockedAirtel.API.Controllers;

[ApiController]
[Route("merchant/v1")]
public class CollectionController : ControllerBase
{
    private readonly ILogger<CollectionController> _logger;
    private readonly ICallbackService _callbackService;

    public CollectionController(ILogger<CollectionController> logger, ICallbackService callbackService)
    {
        _logger = logger;
        _callbackService = callbackService;
    }

    [HttpPost("payments")]
    public async Task<IActionResult> InitiateCollection([FromBody] CollectionRequest request, [FromHeader(Name = "X-Callback-Url")] string callbackUrl)
    {
        // Validate Bearer token
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Unauthorized(new { status = new { code = "401", message = "Unauthorized" } });
        }

        var airtelMoneyId = $"AM{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(1000, 9999)}";

        _logger.LogInformation(
            "Collection request received - TransactionID: {TransactionID}, Amount: {Amount}, Phone: {Phone}",
            request.transaction.id, request.transaction.amount, request.subscriber.msisdn);

        // Send callback asynchronously after a delay
        if (!string.IsNullOrEmpty(callbackUrl))
        {
            _ = _callbackService.SendCollectionCallbackAsync(
                callbackUrl,
                request.transaction.id,
                airtelMoneyId,
                "Transaction processed successfully",
                "TS");
        }

        return Ok(new CollectionResponse
        {
            data = new CollectionData
            {
                transaction = new TransactionDetails
                {
                    id = request.transaction.id,
                    status = "TS"
                }
            },
            status = new ResponseStatus
            {
                code = "200",
                message = "SUCCESS",
                result_code = "ESB000010",
                response_code = "DP00800001006",
                success = true
            }
        });
    }
}
