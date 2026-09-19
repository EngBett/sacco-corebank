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
public class StkPushController : ControllerBase
{
    private readonly ILogger<StkPushController> _logger;
    private readonly IBus _bus;

    public StkPushController(ILogger<StkPushController> logger, IBus bus)
    {
        _logger = logger;
        _bus = bus;
    }

    [HttpPost("stkpush/v1/processrequest")]
    public async Task<IActionResult> ProcessStkPush([FromBody] StkPushRequest request)
    {
        var merchantRequestId = ReferenceGenerator.GenerateMerchantRequestId();
        var checkoutRequestId = ReferenceGenerator.GenerateCheckoutRequestId();

        _logger.LogInformation(
            "STK Push request received - MerchantRequestID: {MerchantRequestID}, CheckoutRequestID: {CheckoutRequestID}, Resuest: {Request}",
            merchantRequestId, checkoutRequestId, request);

        // Publish message to trigger async callback via MassTransit
        await _bus.Publish(new SendStkPushCallbackMessage
        {
            CallbackUrl = request.CallBackURL,
            MerchantRequestId = merchantRequestId,
            CheckoutRequestId = checkoutRequestId,
            Amount = request.Amount,
            PhoneNumber = request.PhoneNumber,
            AccountReference = request.AccountReference,
            ShortCode = request.BusinessShortCode
        });

        return Ok(new StkPushResponse
        {
            MerchantRequestID = merchantRequestId,
            CheckoutRequestID = checkoutRequestId,
            ResponseCode = "0",
            ResponseDescription = "Success. Request accepted for processing",
            CustomerMessage = "Success. Request accepted for processing"
        });
    }
}
