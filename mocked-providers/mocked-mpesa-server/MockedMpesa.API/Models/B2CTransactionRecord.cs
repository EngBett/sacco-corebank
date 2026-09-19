using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MockedMpesa.API.Models;

public class B2CTransactionRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;
    public string ConversationId { get; set; } = null!;
    public string OriginatorConversationId { get; set; } = null!;
    public string TransactionId { get; set; } = null!;
    public decimal Amount { get; set; }
    public string PartyB { get; set; } = null!;
    public string ResultUrl { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? CallbackSentAt { get; set; }
    public string? CallbackStatus { get; set; }
}
