using Microsoft.AspNetCore.Mvc;
using MockedAirtel.API.Models;

namespace MockedAirtel.API.Controllers;

[ApiController]
[Route("standard/v1")]
public class EnquiryController : ControllerBase
{
    private readonly ILogger<EnquiryController> _logger;

    public EnquiryController(ILogger<EnquiryController> logger)
    {
        _logger = logger;
    }

    [HttpGet("payments/{transactionId}")]
    public IActionResult QueryTransaction(string transactionId)
    {
        // Validate Bearer token
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Unauthorized(new { status = new { code = "401", message = "Unauthorized" } });
        }

        _logger.LogInformation("Transaction enquiry received - TransactionID: {TransactionID}", transactionId);

        var airtelMoneyId = $"AM{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(1000, 9999)}";

        return Ok(new EnquiryResponse
        {
            data = new EnquiryData
            {
                transaction = new TransactionEnquiry
                {
                    airtel_money_fee = 10.00m,
                    airtel_money_id = airtelMoneyId,
                    amount = 1000.00m,
                    currency = "KES",
                    id = transactionId,
                    message = "Transaction processed successfully",
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

    [HttpGet("balance")]
    public IActionResult QueryBalance()
    {
        // Validate Bearer token
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Unauthorized(new { status = new { code = "401", message = "Unauthorized" } });
        }

        _logger.LogInformation("Balance enquiry received");

        return Ok(new BalanceResponse
        {
            data = new BalanceData
            {
                available_balance = "500000.00",
                currency = "KES"
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
