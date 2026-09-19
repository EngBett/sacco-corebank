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
public class B2BController : ControllerBase
{
    private readonly ILogger<B2BController> _logger;
    private readonly IBus _bus;

    public B2BController(ILogger<B2BController> logger, IBus bus)
    {
        _logger = logger;
        _bus = bus;
    }

    [HttpPost("b2b/v1/paymentrequest")]
    public async Task<IActionResult> ProcessB2B([FromBody] B2CRequest request)
    {
        var conversationId = ReferenceGenerator.GenerateConversationId();
        var originatorConversationId = Guid.NewGuid().ToString();

        _logger.LogInformation(
            "B2B request received - ConversationID: {ConversationID}, Amount: {Amount}, PartyB: {PartyB}",
            conversationId, request.Amount, request.PartyB);

        // Publish message to trigger async callback via MassTransit
        await _bus.Publish(new SendB2CCallbackMessage
        {
            ResultUrl = request.ResultURL,
            ConversationId = conversationId,
            OriginatorConversationId = originatorConversationId,
            TransactionId = ReferenceGenerator.GenerateTransactionId(),
            Amount = request.Amount,
            ReceiverPartyPublicName = request.PartyB
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

[ApiController]
[Route("mpesa")]
[ValidateBearerToken]
public class B2CController : ControllerBase
{
    private readonly ILogger<B2CController> _logger;
    private readonly IBus _bus;

    public B2CController(ILogger<B2CController> logger, IBus bus)
    {
        _logger = logger;
        _bus = bus;
    }

    [HttpPost("b2c/v1/paymentrequest")]
    public async Task<IActionResult> ProcessB2C([FromBody] B2CRequest request)
    {
        var conversationId = ReferenceGenerator.GenerateConversationId();
        var originatorConversationId = Guid.NewGuid().ToString();

        _logger.LogInformation(
            "B2C request received - ConversationID: {ConversationID}, Amount: {Amount}, PartyB: {PartyB}",
            conversationId, request.Amount, request.PartyB);

        // Publish message to trigger async callback via MassTransit
        await _bus.Publish(new SendB2CCallbackMessage
        {
            ResultUrl = request.ResultURL,
            ConversationId = conversationId,
            OriginatorConversationId = originatorConversationId,
            TransactionId = ReferenceGenerator.GenerateTransactionId(),
            Amount = request.Amount,
            ReceiverPartyPublicName = request.PartyB
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
