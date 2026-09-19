using Microsoft.AspNetCore.Mvc;
using MockedAirtel.API.Models;
using MockedAirtel.API.Services;

namespace MockedAirtel.API.Controllers;

[ApiController]
[Route("standard/v1")]
public class DisbursementController : ControllerBase
{
    private readonly ILogger<DisbursementController> _logger;
    private readonly ICallbackService _callbackService;

    public DisbursementController(ILogger<DisbursementController> logger, ICallbackService callbackService)
    {
        _logger = logger;
        _callbackService = callbackService;
    }

    [HttpPost("disbursements")]
    public async Task<IActionResult> InitiateDisbursement([FromBody] DisbursementRequest request, [FromHeader(Name = "X-Callback-Url")] string callbackUrl)
    {
        // Validate Bearer token
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Unauthorized(new { status = new { code = "401", message = "Unauthorized" } });
        }

        var airtelMoneyId = $"AM{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(1000, 9999)}";

        _logger.LogInformation(
            "Disbursement request received - TransactionID: {TransactionID}, Amount: {Amount}, Payee: {Payee}",
            request.transaction.id, request.transaction.amount, request.payee.msisdn);

        // Send callback asynchronously after a delay
        if (!string.IsNullOrEmpty(callbackUrl))
        {
            _ = _callbackService.SendDisbursementCallbackAsync(
                callbackUrl,
                request.transaction.id,
                airtelMoneyId,
                "Disbursement processed successfully",
                "TS");
        }

        return Ok(new DisbursementResponse
        {
            data = new DisbursementData
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
