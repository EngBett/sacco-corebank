using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MockedMpesa.API.Models;

/// <summary>
/// Record of all transactions processed by the mock M-Pesa service
/// </summary>
public class TransactionRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;
    public string MerchantRequestId { get; set; } = null!;
    public string? CheckoutRequestId { get; set; }
    public string? TransactionId { get; set; }
    public string TransactionType { get; set; } = null!; // StkPush, B2C, Reversal, etc.
    public string? PhoneNumber { get; set; }
    public decimal Amount { get; set; }
    public string? AccountReference { get; set; }
    public string? CallbackUrl { get; set; }
    public string? ShortCode { get; set; }
    public string? MpesaReceiptNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CallbackSentAt { get; set; }
    public string? CallbackStatus { get; set; } // Success, Failed, Skipped
    
    /// <summary>
    /// When true, callback will return ResultCode != 0 (failure)
    /// </summary>
    public bool SimulateFailure { get; set; }
    
    /// <summary>
    /// When true, callback will not be sent (simulate missed callback)
    /// </summary>
    public bool SkipCallback { get; set; }
}
