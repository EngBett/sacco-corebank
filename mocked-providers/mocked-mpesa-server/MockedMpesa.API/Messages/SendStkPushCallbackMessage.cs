namespace MockedMpesa.API.Messages;

public record SendStkPushCallbackMessage
{
    public string CallbackUrl { get; init; } = string.Empty;
    public string MerchantRequestId { get; init; } = string.Empty;
    public string CheckoutRequestId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string PhoneNumber { get; init; } = string.Empty;
    public string AccountReference { get; init; } = string.Empty;
    public string ShortCode { get; init; } = string.Empty;
}
