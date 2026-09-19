namespace MockedAirtel.API.Models;

// OAuth Models
public class TokenResponse
{
    public string access_token { get; set; } = string.Empty;
    public string token_type { get; set; } = "Bearer";
    public int expires_in { get; set; } = 3600;
}

// Money Transfer (Collection) Models
public class CollectionRequest
{
    public Reference reference { get; set; } = new();
    public Subscriber subscriber { get; set; } = new();
    public Transaction transaction { get; set; } = new();
}

public class Reference
{
    public string id { get; set; } = string.Empty;
}

public class Subscriber
{
    public string country { get; set; } = "KE";
    public string currency { get; set; } = "KES";
    public string msisdn { get; set; } = string.Empty;
}

public class Transaction
{
    public decimal amount { get; set; }
    public string country { get; set; } = "KE";
    public string currency { get; set; } = "KES";
    public string id { get; set; } = string.Empty;
}

public class CollectionResponse
{
    public CollectionData data { get; set; } = new();
    public ResponseStatus status { get; set; } = new();
}

public class CollectionData
{
    public TransactionDetails transaction { get; set; } = new();
}

public class TransactionDetails
{
    public string id { get; set; } = string.Empty;
    public string status { get; set; } = "TS";
}

public class ResponseStatus
{
    public string code { get; set; } = "200";
    public string message { get; set; } = "SUCCESS";
    public string result_code { get; set; } = "ESB000010";
    public string response_code { get; set; } = "DP00800001006";
    public bool success { get; set; } = true;
}

// Disbursement Models
public class DisbursementRequest
{
    public Payee payee { get; set; } = new();
    public Reference reference { get; set; } = new();
    public Transaction transaction { get; set; } = new();
}

public class Payee
{
    public string msisdn { get; set; } = string.Empty;
}

public class DisbursementResponse
{
    public DisbursementData data { get; set; } = new();
    public ResponseStatus status { get; set; } = new();
}

public class DisbursementData
{
    public TransactionDetails transaction { get; set; } = new();
}

// Refund Models
public class RefundRequest
{
    public RefundTransaction transaction { get; set; } = new();
}

public class RefundTransaction
{
    public string airtel_money_id { get; set; } = string.Empty;
    public string country { get; set; } = "KE";
    public string currency { get; set; } = "KES";
}

public class RefundResponse
{
    public RefundData data { get; set; } = new();
    public ResponseStatus status { get; set; } = new();
}

public class RefundData
{
    public TransactionDetails transaction { get; set; } = new();
}

// Transaction Enquiry Models
public class EnquiryResponse
{
    public EnquiryData data { get; set; } = new();
    public ResponseStatus status { get; set; } = new();
}

public class EnquiryData
{
    public TransactionEnquiry transaction { get; set; } = new();
}

public class TransactionEnquiry
{
    public decimal airtel_money_fee { get; set; }
    public string airtel_money_id { get; set; } = string.Empty;
    public decimal amount { get; set; }
    public string currency { get; set; } = "KES";
    public string id { get; set; } = string.Empty;
    public string message { get; set; } = string.Empty;
    public string status { get; set; } = "TS";
}

// Balance Enquiry Models
public class BalanceResponse
{
    public BalanceData data { get; set; } = new();
    public ResponseStatus status { get; set; } = new();
}

public class BalanceData
{
    public string available_balance { get; set; } = "500000.00";
    public string currency { get; set; } = "KES";
}

// Callback/Webhook Models
public class CollectionCallback
{
    public TransactionCallback transaction { get; set; } = new();
}

public class TransactionCallback
{
    public string id { get; set; } = string.Empty;
    public string message { get; set; } = string.Empty;
    public string status_code { get; set; } = "TS";
    public string airtel_money_id { get; set; } = string.Empty;
}
