namespace MockedMpesa.API.Messages;

public record SendTransactionStatusCallbackMessage
{
    public string ResultUrl { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;
    public string OriginatorConversationId { get; init; } = string.Empty;
    public string TransactionId { get; init; } = string.Empty;
    public string OriginalConversationId { get; init; } = string.Empty;
}
