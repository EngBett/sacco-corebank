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
public class AccountBalanceController : ControllerBase
{
    private readonly ILogger<AccountBalanceController> _logger;
    private readonly IBus _bus;

    public AccountBalanceController(ILogger<AccountBalanceController> logger, IBus bus)
    {
        _logger = logger;
        _bus = bus;
    }

    [HttpPost("accountbalance/v1/query")]
    public async Task<IActionResult> QueryAccountBalance([FromBody] AccountBalanceRequest request)
    {
        var conversationId = ReferenceGenerator.GenerateConversationId();
        var originatorConversationId = Guid.NewGuid().ToString();

        _logger.LogInformation(
            "Account balance query received - ConversationID: {ConversationID}, PartyA: {PartyA}",
            conversationId, request.PartyA);

        // Publish message to trigger async callback via MassTransit
        await _bus.Publish(new SendAccountBalanceCallbackMessage
        {
            ResultUrl = request.ResultURL,
            ConversationId = conversationId,
            OriginatorConversationId = originatorConversationId,
            PartyA = request.PartyA
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
