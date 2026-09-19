using MassTransit;
using MockedMpesa.API.Messages;
using MockedMpesa.API.Services;

namespace MockedMpesa.API.Consumers;

public class SendStkPushCallbackConsumer : IConsumer<SendStkPushCallbackMessage>
{
    private readonly ICallbackService _callbackService;
    private readonly ILogger<SendStkPushCallbackConsumer> _logger;

    public SendStkPushCallbackConsumer(ICallbackService callbackService, ILogger<SendStkPushCallbackConsumer> logger)
    {
        _callbackService = callbackService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SendStkPushCallbackMessage> context)
    {
        var message = context.Message;
        
        _logger.LogInformation(
            "Processing STK Push callback - Messsage : {Message}", message);

        await _callbackService.SendStkPushCallbackAsync(
            message.CallbackUrl,
            message.MerchantRequestId,
            message.CheckoutRequestId,
            message.Amount,
            message.PhoneNumber,
            message.AccountReference,
            message.ShortCode);
    }
}
