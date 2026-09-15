using System.Text.Json.Serialization;

namespace MockedEquity.API.Models;

// Wire shapes from the Finserve "Jenga DFS Partner REST APIs" documentation (v1.0.8).
// Every amount is a string on the wire, matching the real API.

public class AuthRequest
{
    [JsonPropertyName("merchantCode")]
    public string? MerchantCode { get; set; }

    [JsonPropertyName("consumerSecret")]
    public string? ConsumerSecret { get; set; }
}

public class AuthResponse
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Absolute UTC expiry, as the real API returns (not a duration).</summary>
    [JsonPropertyName("expiresIn")]
    public string ExpiresIn { get; set; } = string.Empty;

    [JsonPropertyName("issuedAt")]
    public string IssuedAt { get; set; } = string.Empty;

    [JsonPropertyName("tokenType")]
    public string TokenType { get; set; } = "Bearer";
}

/// <summary>Returned by every payment endpoint. Deliberately carries no transaction id.</summary>
public class AcknowledgmentResponse
{
    [JsonPropertyName("responseCode")]
    public string ResponseCode { get; set; } = "0";

    [JsonPropertyName("responseDesc")]
    public string ResponseDesc { get; set; } = "Request accepted successfully";

    [JsonPropertyName("serviceStatus")]
    public string ServiceStatus { get; set; } = "PENDING";
}

public class GenericResponse
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = "00";

    [JsonPropertyName("metadata")]
    public Dictionary<string, string?> Metadata { get; set; } = new();
}

public class C2BRequest
{
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    [JsonPropertyName("amount")]
    public string? Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("shortCode")]
    public string? ShortCode { get; set; }

    [JsonPropertyName("pin")]
    public string? Pin { get; set; }

    [JsonPropertyName("callbackUrl")]
    public string? CallbackUrl { get; set; }

    [JsonPropertyName("msisdn")]
    public string? Msisdn { get; set; }

    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }
}

public class PaybillRequest : C2BRequest
{
    [JsonPropertyName("billerCode")]
    public string? BillerCode { get; set; }

    [JsonPropertyName("billerName")]
    public string? BillerName { get; set; }

    [JsonPropertyName("billPaymentReference")]
    public string? BillPaymentReference { get; set; }
}

public class B2CRequest
{
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    [JsonPropertyName("amount")]
    public string? Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("pin")]
    public string? Pin { get; set; }

    [JsonPropertyName("shortCode")]
    public string? ShortCode { get; set; }

    [JsonPropertyName("msisdn")]
    public string? Msisdn { get; set; }

    [JsonPropertyName("callbackUrl")]
    public string? CallbackUrl { get; set; }

    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }
}

public class B2BRequest
{
    [JsonPropertyName("transactionReference")]
    public string? TransactionReference { get; set; }

    [JsonPropertyName("amount")]
    public string? Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("initiatorShortCode")]
    public string? InitiatorShortCode { get; set; }

    [JsonPropertyName("tillNumber")]
    public string? TillNumber { get; set; }

    [JsonPropertyName("referenceData")]
    public B2BReferenceData? ReferenceData { get; set; }

    [JsonPropertyName("receiverOrgShortCode")]
    public string? ReceiverOrgShortCode { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("callbackUrl")]
    public string? CallbackUrl { get; set; }
}

public class B2BReferenceData
{
    [JsonPropertyName("invoiceNumber")]
    public string? InvoiceNumber { get; set; }

    [JsonPropertyName("customerName")]
    public string? CustomerName { get; set; }

    [JsonPropertyName("accountNumber")]
    public string? AccountNumber { get; set; }

    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }
}

public class StatusQueryRequest
{
    [JsonPropertyName("transactionId")]
    public string? TransactionId { get; set; }
}

public class ReversalRequest
{
    [JsonPropertyName("receiptNumber")]
    public string? ReceiptNumber { get; set; }

    [JsonPropertyName("amount")]
    public string? Amount { get; set; }

    [JsonPropertyName("organizationUsername")]
    public string? OrganizationUsername { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("shortCode")]
    public string? ShortCode { get; set; }
}

public class BalanceRequest
{
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    [JsonPropertyName("organizationUsername")]
    public string? OrganizationUsername { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("shortCode")]
    public string? ShortCode { get; set; }

    [JsonPropertyName("accountType")]
    public string? AccountType { get; set; }
}

/// <summary>Flat success callback shape.</summary>
public class SuccessCallback
{
    [JsonPropertyName("transactionReference")]
    public string TransactionReference { get; set; } = string.Empty;

    [JsonPropertyName("resultType")]
    public string ResultType { get; set; } = "SUCCESS";

    [JsonPropertyName("resultCode")]
    public string ResultCode { get; set; } = "00";

    [JsonPropertyName("resultDesc")]
    public string ResultDesc { get; set; } = "Transaction processed successfully";

    [JsonPropertyName("transactionId")]
    public string TransactionId { get; set; } = string.Empty;
}

/// <summary>
/// Nested failure callback shape. The real API renames the echo to metadata.requestReference here,
/// which is exactly the divergence integrators get wrong — so the mock reproduces it faithfully.
/// </summary>
public class FailureCallback
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "3011";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "Transaction failed";

    [JsonPropertyName("metadata")]
    public FailureCallbackMetadata Metadata { get; set; } = new();
}

public class FailureCallbackMetadata
{
    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; set; } = "Transaction failed";

    [JsonPropertyName("requestReference")]
    public string RequestReference { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "FAILED";

    [JsonPropertyName("transactionId")]
    public string TransactionId { get; set; } = string.Empty;
}

/// <summary>What the mock remembers about a transaction, for status queries and reversals.</summary>
public class EquityTransaction
{
    public string RequestId { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "KES";
    public string? Msisdn { get; set; }
    public string? CallbackUrl { get; set; }

    /// <summary>PENDING → SUCCESS / FAILED / REVERSED.</summary>
    public string Status { get; set; } = "PENDING";

    public string? FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
