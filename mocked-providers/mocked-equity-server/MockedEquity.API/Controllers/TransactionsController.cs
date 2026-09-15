using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MockedEquity.API.Models;
using MockedEquity.API.Services;

namespace MockedEquity.API.Controllers;

/// <summary>
/// The Jenga DFS payment and experience APIs, served under /momo-apis to match the real base URL.
/// </summary>
/// <remarks>
/// Every payment endpoint answers immediately with <c>serviceStatus: PENDING</c> and no transaction
/// id — the id only reaches the client on the asynchronous callback. That asymmetry is the whole
/// reason this mock exists, so it is reproduced exactly rather than simplified.
/// </remarks>
[ApiController]
[Route("momo-apis/api/v1/transaction")]
public class TransactionsController : ControllerBase
{
    private readonly IPaymentSimulator _simulator;
    private readonly ITransactionStore _store;
    private readonly JengaOptions _options;
    private readonly ILogger<TransactionsController> _logger;

    public TransactionsController(
        IPaymentSimulator simulator,
        ITransactionStore store,
        IOptions<JengaOptions> options,
        ILogger<TransactionsController> logger)
    {
        _simulator = simulator;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    private string? SimulationHeader =>
        Request.Headers.TryGetValue("X-Simulate", out var value) ? value.FirstOrDefault() : null;

    /// <summary>C2B customer buy-goods.</summary>
    [HttpPost("c2b/customer-initiated-payment")]
    [ProducesResponseType(typeof(AcknowledgmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse), StatusCodes.Status400BadRequest)]
    public IActionResult CustomerInitiatedPayment([FromBody] C2BRequest request) =>
        Submit(new PaymentSubmission(
            request.RequestId, "C2B", request.Amount, request.Currency, request.Msisdn,
            request.CallbackUrl, request.Pin, _options.Pin, SimulationHeader));

    /// <summary>C2B paybill.</summary>
    [HttpPost("c2b/paybill")]
    [ProducesResponseType(typeof(AcknowledgmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse), StatusCodes.Status400BadRequest)]
    public IActionResult PaybillPayment([FromBody] PaybillRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BillerCode))
            return BadRequest(Error("100", "Invalid request data: billerCode is required"));

        if (string.IsNullOrWhiteSpace(request.BillPaymentReference))
            return BadRequest(Error("100", "Invalid request data: billPaymentReference is required"));

        return Submit(new PaymentSubmission(
            request.RequestId, "C2B-PAYBILL", request.Amount, request.Currency, request.Msisdn,
            request.CallbackUrl, request.Pin, _options.Pin, SimulationHeader));
    }

    /// <summary>B2C payout to a customer wallet.</summary>
    [HttpPost("b2c/business-to-customer-payment")]
    [ProducesResponseType(typeof(AcknowledgmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse), StatusCodes.Status400BadRequest)]
    public IActionResult BusinessToCustomer([FromBody] B2CRequest request) =>
        Submit(new PaymentSubmission(
            request.RequestId, "B2C", request.Amount, request.Currency, request.Msisdn,
            request.CallbackUrl, request.Pin, _options.Pin, SimulationHeader));

    /// <summary>B2B payout to another merchant's till.</summary>
    [HttpPost("b2b/buy-goods")]
    [ProducesResponseType(typeof(AcknowledgmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse), StatusCodes.Status400BadRequest)]
    public IActionResult BuyGoods([FromBody] B2BRequest request) =>
        SubmitB2B(request, "B2B-BUY-GOODS");

    /// <summary>B2B payout to another organization's biller account.</summary>
    [HttpPost("b2b/pay-bill")]
    [ProducesResponseType(typeof(AcknowledgmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse), StatusCodes.Status400BadRequest)]
    public IActionResult PayBill([FromBody] B2BRequest request) =>
        SubmitB2B(request, "B2B-PAY-BILL");

    /// <summary>Current status of a transaction, by our request id or Equity's transaction id.</summary>
    [HttpPost("query-transaction-status")]
    [ProducesResponseType(typeof(GenericResponse), StatusCodes.Status200OK)]
    public IActionResult QueryTransactionStatus([FromBody] StatusQueryRequest request)
    {
        var transaction = _store.Find(request.TransactionId);

        if (transaction is null)
        {
            _logger.LogInformation("Status query for unknown transaction {TransactionId}", request.TransactionId);

            // The documented shape for a transaction the platform does not recognise: code 3011 and
            // null identifiers, NOT a 404.
            return Ok(new GenericResponse
            {
                Message = null,
                Code = "3011",
                Metadata = new Dictionary<string, string?>
                {
                    ["requestReference"] = null,
                    ["transactionId"] = null,
                    ["status"] = "FAILED"
                }
            });
        }

        _logger.LogInformation(
            "Status query for {TransactionId} → {Status}", request.TransactionId, transaction.Status);

        return Ok(new GenericResponse
        {
            Message = "Your request was successful.",
            Code = "00",
            Metadata = new Dictionary<string, string?>
            {
                ["requestReference"] = transaction.RequestId,
                ["transactionId"] = transaction.TransactionId,
                ["status"] = transaction.Status
            }
        });
    }

    /// <summary>Reverses a completed transaction by receipt number.</summary>
    [HttpPost("reverse-transaction")]
    [ProducesResponseType(typeof(GenericResponse), StatusCodes.Status200OK)]
    public IActionResult ReverseTransaction([FromBody] ReversalRequest request)
    {
        if (request.OrganizationUsername != _options.OrganizationUsername)
            return Unauthorized(Error("101", "Unauthorized or invalid credentials: unknown organizationUsername"));

        var transaction = _store.Find(request.ReceiptNumber);

        if (transaction is null)
        {
            return Ok(new GenericResponse
            {
                Message = "Transaction not found",
                Code = "102",
                Metadata = new Dictionary<string, string?> { ["status"] = "FAILED" }
            });
        }

        if (!string.Equals(transaction.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new GenericResponse
            {
                Message = $"Only completed transactions can be reversed (current status: {transaction.Status})",
                Code = "102",
                Metadata = new Dictionary<string, string?> { ["status"] = "FAILED" }
            });
        }

        var originalAmount = transaction.Amount;
        transaction.Status = "REVERSED";
        _store.Update(transaction);

        _logger.LogInformation(
            "Reversed {TransactionId} ({RequestId}) for {Amount}",
            transaction.TransactionId, transaction.RequestId, originalAmount);

        return Ok(new GenericResponse
        {
            Message = "Your request was successful.",
            Code = "00",
            Metadata = new Dictionary<string, string?>
            {
                ["originalAmount"] = originalAmount.ToString("0.00"),
                ["transactionStatus"] = "Completed",
                ["receiptNumber"] = transaction.TransactionId,
                ["status"] = "SUCCESS"
            }
        });
    }

    /// <summary>Organization float balance.</summary>
    [HttpPost("organization/balance")]
    [ProducesResponseType(typeof(GenericResponse), StatusCodes.Status200OK)]
    public IActionResult QueryBalance([FromBody] BalanceRequest request)
    {
        if (request.OrganizationUsername != _options.OrganizationUsername)
            return Unauthorized(Error("101", "Unauthorized or invalid credentials: unknown organizationUsername"));

        // A stable, obviously-fake float so tests can assert on it.
        return Ok(new GenericResponse
        {
            Message = "Your request was successful.",
            Code = "00",
            Metadata = new Dictionary<string, string?>
            {
                ["accountStatus"] = "Active",
                ["accountHolderId"] = _options.ShortCode,
                ["accountTypeId"] = "22013",
                ["accountTypeAlias"] = "Organization Working Account",
                ["accountName"] = "DefaultAccount",
                ["accountNo"] = "500000000110371555",
                ["currency"] = "KES",
                ["currentBalance"] = "1000000.00",
                ["availableBalance"] = "1000000.00",
                ["reservedBalance"] = "0.00",
                ["unclearedBalance"] = "0.00",
                ["status"] = "SUCCESS"
            }
        });
    }

    private IActionResult SubmitB2B(B2BRequest request, string type)
    {
        if (string.IsNullOrWhiteSpace(request.TillNumber))
            return BadRequest(Error("100", "Invalid request data: tillNumber is required"));

        if (request.ReferenceData is null)
            return BadRequest(Error("100", "Invalid request data: referenceData is required"));

        return Submit(new PaymentSubmission(
            request.TransactionReference, type, request.Amount, request.Currency, Msisdn: null,
            request.CallbackUrl, request.Password, _options.OrganizationPassword, SimulationHeader));
    }

    private IActionResult Submit(PaymentSubmission submission)
    {
        var outcome = _simulator.Accept(submission);

        return outcome.Rejection is { } rejection
            ? StatusCode(rejection.StatusCode, rejection.Body)
            : Ok(outcome.Acknowledgment);
    }

    private static GenericResponse Error(string code, string message) =>
        new()
        {
            Message = message,
            Code = code,
            Metadata = new Dictionary<string, string?>
            {
                ["status"] = "FAILED",
                ["errorMessage"] = message
            }
        };
}
