using Microsoft.AspNetCore.Mvc;
using MockedMpesa.API.Attributes;
using MockedMpesa.API.Models;

namespace MockedMpesa.API.Controllers;

[ApiController]
[Route("pulltransactions")]
[ValidateBearerToken]
public class PullTransactionsController : ControllerBase
{
    private readonly ILogger<PullTransactionsController> _logger;

    public PullTransactionsController(ILogger<PullTransactionsController> logger)
    {
        _logger = logger;
    }

    [HttpPost("v1/register")]
    public IActionResult RegisterUrl([FromBody] PullTransactionRegisterRequest request)
    {
        _logger.LogInformation(
            "Pull transaction register request received - ShortCode: {ShortCode}",
            request.ShortCode);

        return Ok(new PullTransactionResponse
        {
            ResponseCode = "0",
            ResponseDescription = "Success",
            RequestId = $"REG-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}"
        });
    }

    [HttpPost("v1/query")]
    public IActionResult QueryTransactions([FromBody] PullTransactionQueryRequest request)
    {
        _logger.LogInformation(
            "Pull transaction query received - ShortCode: {ShortCode}, StartDate: {StartDate}, EndDate: {EndDate}",
            request.ShortCode, request.StartDate, request.EndDate);

        // Return mock transactions
        return Ok(new PullTransactionQueryResponse
        {
            ResponseCode = "0",
            ResponseDescription = "Success",
            Transactions = new List<PullTransaction>
            {
                new()
                {
                    TransactionType = "Pay Bill",
                    TransID = $"QGR{Random.Shared.Next(10000000, 99999999)}",
                    TransTime = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                    TransAmount = 1000.00m,
                    BusinessShortCode = request.ShortCode,
                    BillRefNumber = "ORDER123",
                    InvoiceNumber = "",
                    OrgAccountBalance = 50000.00m,
                    ThirdPartyTransID = "",
                    MSISDN = "254712345678",
                    FirstName = "John",
                    MiddleName = "",
                    LastName = "Doe"
                }
            }
        });
    }
}
