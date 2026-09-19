using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MockedMpesa.API.Models;

public class C2BRegistration
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;
    public string ShortCode { get; set; } = null!;
    public string? ValidationUrl { get; set; }
    public string? ConfirmationUrl { get; set; }
    public string ResponseType { get; set; } = "Completed"; // Completed or Cancelled
    public DateTime RegisteredAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class C2BRegisterRequest
{
    public string ShortCode { get; set; } = null!;
    public string? ValidationURL { get; set; }
    public string? ConfirmationURL { get; set; }
    public string ResponseType { get; set; } = "Completed";
}

public class C2BRegisterResponse
{
    public string ConversationID { get; set; } = null!;
    public string OriginatorCoversationID { get; set; } = null!;
    public string ResponseDescription { get; set; } = null!;
}

public class C2BValidationRequest
{
    public string TransactionType { get; set; } = null!;
    public string TransID { get; set; } = null!;
    public string TransTime { get; set; } = null!;
    public string TransAmount { get; set; } = null!;
    public string BusinessShortCode { get; set; } = null!;
    public string BillRefNumber { get; set; } = null!;
    public string? InvoiceNumber { get; set; }
    public string? OrgAccountBalance { get; set; }
    public string? ThirdPartyTransID { get; set; }
    public string MSISDN { get; set; } = null!;
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
}
