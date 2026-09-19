namespace MockedMpesa.API.Messages;

public record SendAccountBalanceCallbackMessage
{
    public string ResultUrl { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;
    public string OriginatorConversationId { get; init; } = string.Empty;
    public string PartyA { get; init; } = string.Empty;
}
