using System.Text.Json.Serialization;

namespace MockedMpesa.API.Models;

// OAuth Models
public class TokenResponse
{
    public string access_token { get; set; } = string.Empty;
    public string expires_in { get; set; } = "3599";
}

// STK Push Models
public class StkPushRequest
{
    public string BusinessShortCode { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public string TransactionType { get; set; } = "CustomerPayBillOnline";
    public decimal Amount { get; set; }
    public string PartyA { get; set; } = string.Empty;
    public string PartyB { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string CallBackURL { get; set; } = string.Empty;
    public string AccountReference { get; set; } = string.Empty;
    public string TransactionDesc { get; set; } = string.Empty;
}

public class StkPushResponse
{
    public string MerchantRequestID { get; set; } = string.Empty;
    public string CheckoutRequestID { get; set; } = string.Empty;
    public string ResponseCode { get; set; } = "0";
    public string ResponseDescription { get; set; } = "Success. Request accepted for processing";
    public string CustomerMessage { get; set; } = "Success. Request accepted for processing";
}

// B2C Models
public class B2CRequest
{
    public string InitiatorName { get; set; } = string.Empty;
    public string SecurityCredential { get; set; } = string.Empty;
    public string CommandID { get; set; } = "BusinessPayment";
    public decimal Amount { get; set; }
    public string PartyA { get; set; } = string.Empty;
    public string PartyB { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
    public string QueueTimeOutURL { get; set; } = string.Empty;
    public string ResultURL { get; set; } = string.Empty;
    public string Occasion { get; set; } = string.Empty;
}

public class B2CResponse
{
    public string ConversationID { get; set; } = string.Empty;
    public string OriginatorConversationID { get; set; } = string.Empty;
    public string ResponseCode { get; set; } = "0";
    public string ResponseDescription { get; set; } = "Accept the service request successfully.";
}

// Reversal Models
public class ReversalRequest
{
    public string Initiator { get; set; } = string.Empty;
    public string SecurityCredential { get; set; } = string.Empty;
    public string CommandID { get; set; } = "TransactionReversal";
    public string TransactionID { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string ReceiverParty { get; set; } = string.Empty;
    public string RecieverIdentifierType { get; set; } = "11";
    public string Remarks { get; set; } = string.Empty;
    public string QueueTimeOutURL { get; set; } = string.Empty;
    public string ResultURL { get; set; } = string.Empty;
    public string Occasion { get; set; } = string.Empty;
}

public class ReversalResponse
{
    public string ConversationID { get; set; } = string.Empty;
    public string OriginatorConversationID { get; set; } = string.Empty;
    public string ResponseCode { get; set; } = "0";
    public string ResponseDescription { get; set; } = "Accept the service request successfully.";
}

// Callback Models - STK Push
public class StkPushCallbackRoot
{
    [JsonPropertyName("Body")]
    public StkPushCallbackBody Body { get; set; } = new();
}

public class StkPushCallbackBody
{
    [JsonPropertyName("stkCallback")]
    public StkPushCallback StkCallback { get; set; } = new();
}

public class StkPushCallback
{
    [JsonPropertyName("MerchantRequestID")]
    public string MerchantRequestId { get; set; } = string.Empty;
    
    [JsonPropertyName("CheckoutRequestID")]
    public string CheckoutRequestId { get; set; } = string.Empty;
    
    [JsonPropertyName("ResultCode")]
    public int ResultCode { get; set; }
    
    [JsonPropertyName("ResultDesc")]
    public string ResultDesc { get; set; } = string.Empty;
    
    [JsonPropertyName("CallbackMetadata")]
    public CallbackMetadata? CallbackMetadata { get; set; }
}

public class CallbackMetadata
{
    [JsonPropertyName("Item")]
    public List<CallbackItem> Item { get; set; } = new();
}

public class CallbackItem
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("Value")]
    public object? Value { get; set; }
}

// Callback Models - B2C, Reversal, Transaction Status, Account Balance
public class ResultCallbackRoot
{
    [JsonPropertyName("Result")]
    public ResultParameters Result { get; set; } = new();
}

public class ResultParameters
{
    [JsonPropertyName("ResultType")]
    public int ResultType { get; set; }
    
    [JsonPropertyName("ResultCode")]
    public int ResultCode { get; set; }
    
    [JsonPropertyName("ResultDesc")]
    public string ResultDesc { get; set; } = string.Empty;
    
    [JsonPropertyName("OriginatorConversationID")]
    public string OriginatorConversationId { get; set; } = string.Empty;
    
    [JsonPropertyName("ConversationID")]
    public string ConversationId { get; set; } = string.Empty;
    
    [JsonPropertyName("TransactionID")]
    public string TransactionId { get; set; } = string.Empty;
    
    [JsonPropertyName("ReferenceData")]
    public ResultParameterArray? ReferenceData { get; set; }
}

public class ResultParameterArray
{
    [JsonPropertyName("ResultParameter")]
    public List<ResultParameterItem> ResultParameter { get; set; } = new();
}

public class ResultParameterItem
{
    [JsonPropertyName("Key")]
    public string Key { get; set; } = string.Empty;
    
    [JsonPropertyName("Value")]
    public object? Value { get; set; }
}

// Transaction Status Models
public class TransactionStatusRequest
{
    public string Initiator { get; set; } = string.Empty;
    public string SecurityCredential { get; set; } = string.Empty;
    public string CommandID { get; set; } = "TransactionStatusQuery";
    public string TransactionID { get; set; } = string.Empty;
    public string OriginalConversationID { get; set; } = string.Empty;
    public string PartyA { get; set; } = string.Empty;
    public string IdentifierType { get; set; } = "4";
    public string Remarks { get; set; } = string.Empty;
    public string QueueTimeOutURL { get; set; } = string.Empty;
    public string ResultURL { get; set; } = string.Empty;
    public string Occasion { get; set; } = string.Empty;
}

// STK Push Query Models
public class StkPushQueryRequest
{
    public string BusinessShortCode { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public string CheckoutRequestID { get; set; } = string.Empty;
}

public class StkPushQueryResponse
{
    [JsonPropertyName(nameof(ResponseCode))]
    public string ResponseCode { get; set; } = null!;
    
    [JsonPropertyName(nameof(ResponseDescription))]
    public string ResponseDescription { get; set; } = null!;
    
    [JsonPropertyName("MerchantRequestID")]
    public string MerchantRequestId { get; set; } = null!;
    
    [JsonPropertyName("CheckoutRequestID")]
    public string CheckoutRequestId { get; set; } = null!;
    
    [JsonPropertyName(nameof(ResultCode))]
    public string ResultCode { get; set; } = null!;
    
    [JsonPropertyName(nameof(ResultDesc))]
    public string ResultDesc { get; set; } = null!;
    
    [JsonPropertyName(nameof(MpesaReceiptNumber))]
    public string? MpesaReceiptNumber { get; set; }
}

// Account Balance Models
public class AccountBalanceRequest
{
    public string Initiator { get; set; } = string.Empty;
    public string SecurityCredential { get; set; } = string.Empty;
    public string CommandID { get; set; } = "AccountBalance";
    public string PartyA { get; set; } = string.Empty;
    public string IdentifierType { get; set; } = "4";
    public string Remarks { get; set; } = string.Empty;
    public string QueueTimeOutURL { get; set; } = string.Empty;
    public string ResultURL { get; set; } = string.Empty;
}

// Pull Transaction Models
public class PullTransactionRegisterRequest
{
    public string ShortCode { get; set; } = string.Empty;
    public string RequestType { get; set; } = "Pull";
    public string NominatedNumber { get; set; } = string.Empty;
    public string CallBackURL { get; set; } = string.Empty;
}

public class PullTransactionQueryRequest
{
    public string ShortCode { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public string OffSetValue { get; set; } = "0";
}

public class PullTransactionResponse
{
    public string ResponseCode { get; set; } = "0";
    public string ResponseDescription { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
}

public class PullTransactionQueryResponse
{
    public string ResponseCode { get; set; } = "0";
    public string ResponseDescription { get; set; } = string.Empty;
    public List<PullTransaction> Transactions { get; set; } = new();
}

public class PullTransaction
{
    public string TransactionType { get; set; } = string.Empty;
    public string TransID { get; set; } = string.Empty;
    public string TransTime { get; set; } = string.Empty;
    public decimal TransAmount { get; set; }
    public string BusinessShortCode { get; set; } = string.Empty;
    public string BillRefNumber { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal OrgAccountBalance { get; set; }
    public string ThirdPartyTransID { get; set; } = string.Empty;
    public string MSISDN { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}
