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
public class ReversalController : ControllerBase
{
    private readonly ILogger<ReversalController> _logger;
    private readonly IBus _bus;

    public ReversalController(ILogger<ReversalController> logger, IBus bus)
    {
        _logger = logger;
        _bus = bus;
    }

    [HttpPost("reversal/v1/request")]
    public async Task<IActionResult> ProcessReversal([FromBody] ReversalRequest request)
    {
        var conversationId = ReferenceGenerator.GenerateConversationId();
        var originatorConversationId = Guid.NewGuid().ToString();

        _logger.LogInformation(
            "Reversal request received - ConversationID: {ConversationID}, TransactionID: {TransactionID}, Amount: {Amount}",
            conversationId, request.TransactionID, request.Amount);

        // Publish message to trigger async callback via MassTransit
        await _bus.Publish(new SendReversalCallbackMessage
        {
            ResultUrl = request.ResultURL,
            ConversationId = conversationId,
            OriginatorConversationId = originatorConversationId,
            TransactionId = request.TransactionID,
            Amount = request.Amount
        });

        return Ok(new ReversalResponse
        {
            ConversationID = conversationId,
            OriginatorConversationID = originatorConversationId,
            ResponseCode = "0",
            ResponseDescription = "Accept the service request successfully."
        });
    }
}
