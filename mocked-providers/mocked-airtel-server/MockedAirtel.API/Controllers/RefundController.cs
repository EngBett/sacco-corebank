using Microsoft.AspNetCore.Mvc;
using MockedAirtel.API.Models;

namespace MockedAirtel.API.Controllers;

[ApiController]
[Route("standard/v1")]
public class RefundController : ControllerBase
{
    private readonly ILogger<RefundController> _logger;

    public RefundController(ILogger<RefundController> logger)
    {
        _logger = logger;
    }

    [HttpPost("refund")]
    public IActionResult InitiateRefund([FromBody] RefundRequest request)
    {
        // Validate Bearer token
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Unauthorized(new { status = new { code = "401", message = "Unauthorized" } });
        }

        _logger.LogInformation(
            "Refund request received - AirtelMoneyID: {AirtelMoneyID}",
            request.transaction.airtel_money_id);

        return Ok(new RefundResponse
        {
            data = new RefundData
            {
                transaction = new TransactionDetails
                {
                    id = Guid.NewGuid().ToString(),
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
