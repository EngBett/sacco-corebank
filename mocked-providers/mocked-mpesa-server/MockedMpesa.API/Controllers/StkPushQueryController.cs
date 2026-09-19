using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MockedMpesa.API.Attributes;
using MockedMpesa.API.Data;
using MockedMpesa.API.Models;

namespace MockedMpesa.API.Controllers;

[ApiController]
[Route("mpesa")]
[ValidateBearerToken]
public class StkPushQueryController : ControllerBase
{
    private readonly ILogger<StkPushQueryController> _logger;
    private readonly MockMpesaDbContext _dbContext;

    public StkPushQueryController(ILogger<StkPushQueryController> logger, MockMpesaDbContext dbContext)
    {
        _logger = logger;
        _dbContext = dbContext;
    }

    [HttpPost("stkpushquery/v2/query")]
    public async Task<IActionResult> QueryStkPush([FromBody] StkPushQueryRequest request)
    {
        _logger.LogInformation(
            "STK Push query received - CheckoutRequestID: {CheckoutRequestID}",
            request.CheckoutRequestID);

        // Query database for actual transaction
        var filter = Builders<TransactionRecord>.Filter.Eq(t => t.CheckoutRequestId, request.CheckoutRequestID);
        var transaction = await _dbContext.Transactions
            .Find(filter)
            .FirstOrDefaultAsync();

        if (transaction == null)
        {
            // Transaction not found
            return Ok(new StkPushQueryResponse
            {
                ResponseCode = "1",
                ResponseDescription = "Request could not be processed",
                MerchantRequestId = "",
                CheckoutRequestId = request.CheckoutRequestID,
                ResultCode = "1032",
                ResultDesc = "Request cancelled by user",
                MpesaReceiptNumber = null
            });
        }

        // Map CallbackStatus to M-Pesa result codes
        string resultCode;
        string resultDesc;
        
        switch (transaction.CallbackStatus)
        {
            case "Success":
                resultCode = "0";
                resultDesc = "The service request is processed successfully.";
                break;
            case "Failed":
                resultCode = "1";
                resultDesc = "Insufficient funds in the account"; // Match simulation settings default
                break;
            case "Skipped":
                resultCode = "1037";
                resultDesc = "DS timeout user cannot be reached";
                break;
            case "Pending":
            default:
                // Transaction initiated but callback not yet received
                resultCode = "1037";
                resultDesc = "STK Request is pending. Please check after some time.";
                break;
        }

        return Ok(new StkPushQueryResponse
        {
            ResponseCode = "0",
            ResponseDescription = "The service request has been accepted successfully",
            MerchantRequestId = transaction.MerchantRequestId,
            CheckoutRequestId = transaction.CheckoutRequestId,
            ResultCode = resultCode,
            ResultDesc = resultDesc,
            MpesaReceiptNumber = transaction.CallbackStatus == "Success" ? transaction.MpesaReceiptNumber : null
        });
    }
}
