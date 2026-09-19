using MassTransit;
using MockedMpesa.API.Messages;
using MockedMpesa.API.Services;

namespace MockedMpesa.API.Consumers;

public class SendReversalCallbackConsumer : IConsumer<SendReversalCallbackMessage>
{
    private readonly ICallbackService _callbackService;
    private readonly ILogger<SendReversalCallbackConsumer> _logger;

    public SendReversalCallbackConsumer(ICallbackService callbackService, ILogger<SendReversalCallbackConsumer> logger)
    {
        _callbackService = callbackService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SendReversalCallbackMessage> context)
    {
        var message = context.Message;
        
        _logger.LogInformation(
            "Processing Reversal callback - TransactionID: {TransactionID}, ConversationID: {ConversationID}",
            message.TransactionId, message.ConversationId);

        await _callbackService.SendReversalCallbackAsync(
            message.ResultUrl,
            message.ConversationId,
            message.OriginatorConversationId,
            message.TransactionId,
            message.Amount);
    }
}
