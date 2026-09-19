using MassTransit;
using MockedMpesa.API.Messages;
using MockedMpesa.API.Services;

namespace MockedMpesa.API.Consumers;

public class SendTransactionStatusCallbackConsumer : IConsumer<SendTransactionStatusCallbackMessage>
{
    private readonly ICallbackService _callbackService;
    private readonly ILogger<SendTransactionStatusCallbackConsumer> _logger;

    public SendTransactionStatusCallbackConsumer(ICallbackService callbackService, ILogger<SendTransactionStatusCallbackConsumer> logger)
    {
        _callbackService = callbackService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SendTransactionStatusCallbackMessage> context)
    {
        var message = context.Message;
        
        _logger.LogInformation(
            "Processing Transaction Status callback - TransactionID: {TransactionID}, ConversationID: {ConversationID}",
            message.TransactionId, message.ConversationId);

        await _callbackService.SendTransactionStatusCallbackAsync(
            message.ResultUrl,
            message.ConversationId,
            message.OriginatorConversationId,
            message.TransactionId,
            message.OriginalConversationId);
    }
}
