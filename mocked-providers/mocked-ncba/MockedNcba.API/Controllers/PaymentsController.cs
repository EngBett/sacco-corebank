using Microsoft.AspNetCore.Mvc;
using MockedNcba.API.Models;
using MockedNcba.API.Services;

namespace MockedNcba.API.Controllers;

/// <summary>
/// Mocks the NCBA unified payment endpoint.
/// POST /api/v1/payments/transfer
/// Auth: API-Key and API-User headers required (validated globally by ApiKeyValidationFilter).
/// Simulation: Set X-Simulate-Error-Code header to force a specific NCBA error code.
/// </summary>
[ApiController]
[Route("api/v1/payments")]
public class PaymentsController : ControllerBase
{
    private readonly ITransactionStore _store;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(ITransactionStore store, ILogger<PaymentsController> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Process a PesaLink interbank transfer.
    /// SUCCESS: returns { "TxnReferenceNo": "NCBA..." } with no ErrorCode.
    /// FAILURE: returns { "ErrorCode": "N", "ErrorDescription": "..." } with no TxnReferenceNo.
    /// </summary>
    [HttpPost("transfer")]
    public IActionResult Transfer([FromBody] NcbaPaymentRequest request)
    {
        _logger.LogInformation(
            "NCBA payment request: TranType={TranType}, Reference={Reference}, Amount={Amount}, " +
            "BranchCode={BranchCode}, BeneficiaryAccountName={BeneficiaryAccountName}",
            request.TranType, request.Reference, request.Amount,
            request.BranchCode, request.BeneficiaryAccountName);

        // ── Simulation override via header ─────────────────────────────────────
        var simulatedErrorCode = Request.Headers[SimulationHeaders.ErrorCode].FirstOrDefault();
        if (!string.IsNullOrEmpty(simulatedErrorCode))
        {
            _logger.LogWarning(
                "Simulated NCBA failure: ErrorCode={ErrorCode} for Reference={Reference}",
                simulatedErrorCode, request.Reference);

            return Ok(new NcbaPaymentResponse
            {
                ErrorCode        = simulatedErrorCode,
                ErrorDescription = NcbaErrorCodes.Describe(simulatedErrorCode),
                TxnReferenceNo   = null
            });
        }

        // ── Validation ─────────────────────────────────────────────────────────
        if (!string.Equals(request.Country, "Kenya", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new NcbaPaymentResponse
            {
                ErrorCode        = NcbaErrorCodes.InvalidCountry,
                ErrorDescription = NcbaErrorCodes.Describe(NcbaErrorCodes.InvalidCountry)
            });
        }

        if (!string.Equals(request.TranType, "Pesalink", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new NcbaPaymentResponse
            {
                ErrorCode        = NcbaErrorCodes.InvalidTranType,
                ErrorDescription = NcbaErrorCodes.Describe(NcbaErrorCodes.InvalidTranType)
            });
        }

        if (!string.Equals(request.Currency, "KES", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new NcbaPaymentResponse
            {
                ErrorCode        = NcbaErrorCodes.CurrencyMismatch,
                ErrorDescription = NcbaErrorCodes.Describe(NcbaErrorCodes.CurrencyMismatch)
            });
        }

        if (request.Amount <= 0)
        {
            return Ok(new NcbaPaymentResponse
            {
                ErrorCode        = NcbaErrorCodes.InvalidAmount,
                ErrorDescription = NcbaErrorCodes.Describe(NcbaErrorCodes.InvalidAmount)
            });
        }

        if (string.IsNullOrWhiteSpace(request.Account))
        {
            return Ok(new NcbaPaymentResponse
            {
                ErrorCode        = NcbaErrorCodes.InvalidAccount,
                ErrorDescription = NcbaErrorCodes.Describe(NcbaErrorCodes.InvalidAccount)
            });
        }

        if (string.IsNullOrWhiteSpace(request.Reference) || request.Reference.Length > 12)
        {
            return Ok(new NcbaPaymentResponse
            {
                ErrorCode        = NcbaErrorCodes.InvalidReference,
                ErrorDescription = NcbaErrorCodes.Describe(NcbaErrorCodes.InvalidReference)
            });
        }

        // ── Duplicate check ────────────────────────────────────────────────────
        if (_store.GetByReference(request.Reference) is not null)
        {
            return Ok(new NcbaPaymentResponse
            {
                ErrorCode        = NcbaErrorCodes.Duplicate,
                ErrorDescription = NcbaErrorCodes.Describe(NcbaErrorCodes.Duplicate)
            });
        }

        // ── Generate TxnReferenceNo and store ──────────────────────────────────
        var txnRef = $"NCBA{DateTime.UtcNow:yyyyMMddHHmmss}{Guid.NewGuid().ToString("N")[..6].ToUpper()}";

        _store.Add(request.Reference, new NcbaTransactionRecord
        {
            Reference          = request.Reference,
            TxnReferenceNo     = txnRef,
            TranType           = request.TranType,
            BeneficiaryAccount = request.BeneficiaryAccountName,
            Amount             = request.Amount,
            Currency           = request.Currency,
            Narration          = request.Narration
        });

        _logger.LogInformation(
            "NCBA payment success: TxnReferenceNo={TxnReferenceNo}, Reference={Reference}",
            txnRef, request.Reference);

        return Ok(new NcbaPaymentResponse
        {
            TxnReferenceNo = txnRef
        });
    }
}
