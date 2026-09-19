using System.Text.Json.Serialization;

namespace MockedNcba.API.Models;

// ─── Payment Request (unified endpoint) ─────────────────────────────────────

public class NcbaPaymentRequest
{
    [JsonPropertyName("BankCode")]
    public string BankCode { get; set; } = string.Empty;

    [JsonPropertyName("BankSwiftCode")]
    public string BankSwiftCode { get; set; } = string.Empty;

    [JsonPropertyName("BranchCode")]
    public string BranchCode { get; set; } = string.Empty;

    [JsonPropertyName("BeneficiaryAccountName")]
    public string BeneficiaryAccountName { get; set; } = string.Empty;

    [JsonPropertyName("BeneficiaryName")]
    public string BeneficiaryName { get; set; } = string.Empty;

    [JsonPropertyName("Country")]
    public string Country { get; set; } = string.Empty;

    [JsonPropertyName("TranType")]
    public string TranType { get; set; } = string.Empty;

    /// <summary>Caller's own reference — max 12 characters.</summary>
    [JsonPropertyName("Reference")]
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("Currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("Account")]
    public string Account { get; set; } = string.Empty;

    /// <summary>Numeric decimal amount (not a string).</summary>
    [JsonPropertyName("Amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("Narration")]
    public string Narration { get; set; } = string.Empty;

    [JsonPropertyName("Transaction Date")]
    public string? TransactionDate { get; set; }
}

// ─── Payment Response ────────────────────────────────────────────────────────

public class NcbaPaymentResponse
{
    [JsonPropertyName("ErrorCode")]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("ErrorDescription")]
    public string? ErrorDescription { get; set; }

    [JsonPropertyName("TxnReferenceNo")]
    public string? TxnReferenceNo { get; set; }
}

// ─── Simulation Headers ──────────────────────────────────────────────────────

public static class SimulationHeaders
{
    /// <summary>Set this header to force a specific NCBA error code in the response.</summary>
    public const string ErrorCode = "X-Simulate-Error-Code";
}

// ─── Error Codes ─────────────────────────────────────────────────────────────

public static class NcbaErrorCodes
{
    public const string SystemError      = "0";
    public const string InvalidCountry   = "1";
    public const string InvalidTranType  = "2";
    public const string InvalidAmount    = "3";
    public const string InvalidAccount   = "4";
    public const string InvalidReference = "5";
    public const string Duplicate        = "11";
    public const string InsufficientFunds = "12";
    public const string CurrencyMismatch = "14";

    public static string Describe(string errorCode) => errorCode switch
    {
        "0"  => "System Error",
        "1"  => "Invalid Country",
        "2"  => "Invalid Transaction Type",
        "3"  => "Invalid Amount",
        "4"  => "Invalid Account",
        "5"  => "Invalid Reference",
        "11" => "Duplicate Transaction",
        "12" => "Insufficient Funds",
        "14" => "Currency Mismatch",
        _    => $"Unknown error code: {errorCode}"
    };
}
