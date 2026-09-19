using MassTransit;
using Microsoft.AspNetCore.Mvc;
using MockedMpesa.API.Attributes;
using MockedMpesa.API.Messages;
using MockedMpesa.API.Models;
using MockedMpesa.API.Utilities;

namespace MockedMpesa.API.Controllers;

[ApiController]
[Route("mpesa")]
[ValidateBearerToken]
public class TransactionStatusController : ControllerBase
{
    private readonly ILogger<TransactionStatusController> _logger;
    private readonly IBus _bus;

    public TransactionStatusController(ILogger<TransactionStatusController> logger, IBus bus)
    {
        _logger = logger;
        _bus = bus;
    }

    [HttpPost("transactionstatus/v1/query")]
    public async Task<IActionResult> QueryTransactionStatus([FromBody] TransactionStatusRequest request)
    {
        var conversationId = ReferenceGenerator.GenerateConversationId();
        var originatorConversationId = Guid.NewGuid().ToString();

        _logger.LogInformation(
            "Transaction status query received - ConversationID: {ConversationID}, TransactionID: {TransactionID}",
            conversationId, request.TransactionID);

        // Publish message to trigger async callback via MassTransit
        await _bus.Publish(new SendTransactionStatusCallbackMessage
        {
            ResultUrl = request.ResultURL,
            ConversationId = conversationId,
            OriginatorConversationId = originatorConversationId,
            TransactionId = request.TransactionID,
            OriginalConversationId = request.OriginalConversationID
        });

        return Ok(new B2CResponse
        {
            ConversationID = conversationId,
            OriginatorConversationID = originatorConversationId,
            ResponseCode = "0",
            ResponseDescription = "Accept the service request successfully."
        });
    }
}
