using MassTransit;
using MockedMpesa.API.Messages;
using MockedMpesa.API.Services;

namespace MockedMpesa.API.Consumers;

public class SendB2CCallbackConsumer : IConsumer<SendB2CCallbackMessage>
{
    private readonly ICallbackService _callbackService;
    private readonly ILogger<SendB2CCallbackConsumer> _logger;

    public SendB2CCallbackConsumer(ICallbackService callbackService, ILogger<SendB2CCallbackConsumer> logger)
    {
        _callbackService = callbackService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SendB2CCallbackMessage> context)
    {
        var message = context.Message;
        
        _logger.LogInformation(
            "Processing B2C callback - TransactionID: {TransactionID}, ConversationID: {ConversationID}",
            message.TransactionId, message.ConversationId);

        await _callbackService.SendB2CCallbackAsync(
            message.ResultUrl,
            message.ConversationId,
            message.OriginatorConversationId,
            message.TransactionId,
            message.Amount,
            message.ReceiverPartyPublicName);
    }
}
