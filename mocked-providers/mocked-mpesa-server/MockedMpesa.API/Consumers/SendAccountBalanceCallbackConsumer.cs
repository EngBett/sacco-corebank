using MassTransit;
using MockedMpesa.API.Messages;
using MockedMpesa.API.Services;

namespace MockedMpesa.API.Consumers;

public class SendAccountBalanceCallbackConsumer : IConsumer<SendAccountBalanceCallbackMessage>
{
    private readonly ICallbackService _callbackService;
    private readonly ILogger<SendAccountBalanceCallbackConsumer> _logger;

    public SendAccountBalanceCallbackConsumer(ICallbackService callbackService, ILogger<SendAccountBalanceCallbackConsumer> logger)
    {
        _callbackService = callbackService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SendAccountBalanceCallbackMessage> context)
    {
        var message = context.Message;
        
        _logger.LogInformation(
            "Processing Account Balance callback - ConversationID: {ConversationID}, PartyA: {PartyA}",
            message.ConversationId, message.PartyA);

        await _callbackService.SendAccountBalanceCallbackAsync(
            message.ResultUrl,
            message.ConversationId,
            message.OriginatorConversationId,
            message.PartyA);
    }
}
