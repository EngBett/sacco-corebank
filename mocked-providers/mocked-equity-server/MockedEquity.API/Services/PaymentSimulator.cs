using Microsoft.Extensions.Options;
using MockedEquity.API.Models;
using MockedEquity.API.Security;

namespace MockedEquity.API.Services;

/// <summary>
/// Shared validation and acceptance logic for every payment endpoint: credential checks, requestId
/// format, duplicate detection, and the deterministic failure triggers tests rely on.
/// </summary>
public interface IPaymentSimulator
{
    /// <summary>
    /// Validates and records an incoming payment. Returns the acknowledgement to send back, or a
    /// rejection when the request is not acceptable.
    /// </summary>
    SimulationOutcome Accept(PaymentSubmission submission);
}

/// <param name="RequestId">The requestId / transactionReference field, whichever the endpoint uses.</param>
/// <param name="Type">C2B, C2B-PAYBILL, B2C, B2B-BUY-GOODS or B2B-PAY-BILL.</param>
/// <param name="EncryptedCredential">The pin or password field, still encrypted.</param>
/// <param name="ExpectedCredential">Plaintext the credential must decrypt to.</param>
/// <param name="SimulationHeader">Value of X-Simulate, if the caller set one.</param>
public record PaymentSubmission(
    string? RequestId,
    string Type,
    string? Amount,
    string? Currency,
    string? Msisdn,
    string? CallbackUrl,
    string? EncryptedCredential,
    string ExpectedCredential,
    string? SimulationHeader);

/// <param name="Rejection">Non-null when the request must be refused; the acknowledgement otherwise.</param>
public record SimulationOutcome(
    AcknowledgmentResponse? Acknowledgment,
    RejectionResult? Rejection,
    EquityTransaction? Transaction);

/// <param name="StatusCode">HTTP status to answer with.</param>
public record RejectionResult(int StatusCode, GenericResponse Body);

public class PaymentSimulator : IPaymentSimulator
{
    private readonly ITransactionStore _store;
    private readonly ICallbackService _callbacks;
    private readonly JengaOptions _options;
    private readonly ILogger<PaymentSimulator> _logger;

    public PaymentSimulator(
        ITransactionStore store,
        ICallbackService callbacks,
        IOptions<JengaOptions> options,
        ILogger<PaymentSimulator> logger)
    {
        _store = store;
        _callbacks = callbacks;
        _options = options.Value;
        _logger = logger;
    }

    public SimulationOutcome Accept(PaymentSubmission submission)
    {
        if (string.IsNullOrWhiteSpace(submission.RequestId))
            return Reject("100", "Invalid request data: requestId is required");

        // The shortCode_ prefix is mandatory on the real API and is easy to get wrong, so enforce it.
        if (!submission.RequestId.Contains('_'))
        {
            return Reject("100",
                $"Invalid request data: requestId must be formatted shortCode_uniqueRequestId "
                + $"(received '{submission.RequestId}')");
        }

        if (!decimal.TryParse(submission.Amount, out var amount) || amount <= 0)
            return Reject("100", $"Invalid request data: amount '{submission.Amount}' is not a positive number");

        var currency = string.IsNullOrWhiteSpace(submission.Currency) ? "KES" : submission.Currency;
        if (!IsSupportedCurrency(currency))
            return Reject("3013", "The specified currency unit is incorrect and does not meet the ISO4217 standard.");

        if (_options.ValidateEncryptedCredentials)
        {
            if (!JengaCredentialCipher.TryDecrypt(submission.EncryptedCredential, _options.ApiKey, out var plain))
            {
                _logger.LogWarning(
                    "Credential on {RequestId} could not be decrypted — check the AES-256-GCM envelope "
                    + "(Base64 of IV(12) || SALT(16) || CIPHERTEXT || TAG(16), PBKDF2 over the API key).",
                    submission.RequestId);

                return Reject("101", "Unauthorized or invalid credentials: credential failed to decrypt");
            }

            if (!string.Equals(plain, submission.ExpectedCredential, StringComparison.Ordinal))
            {
                _logger.LogWarning("Credential on {RequestId} decrypted but did not match", submission.RequestId);
                return Reject("101", "Unauthorized or invalid credentials: incorrect PIN or password");
            }
        }

        var transaction = new EquityTransaction
        {
            RequestId = submission.RequestId,
            TransactionId = GenerateTransactionId(),
            Type = submission.Type,
            Amount = amount,
            Currency = currency,
            Msisdn = submission.Msisdn,
            CallbackUrl = submission.CallbackUrl
        };

        if (!_store.TryAdd(transaction))
        {
            // Same behaviour as the real gateway on a reused requestId — this is what makes our
            // retry paths safe to test.
            return Reject("100", $"Invalid request data: requestId '{submission.RequestId}' has already been used");
        }

        var simulation = ResolveSimulation(submission, amount);

        if (simulation is Simulation.RejectAtAck)
        {
            transaction.Status = "FAILED";
            transaction.FailureCode = "103";
            transaction.FailureReason = "Internal processing error (simulated)";
            _store.Update(transaction);

            _logger.LogInformation("Simulating acknowledgement rejection for {RequestId}", transaction.RequestId);

            return new SimulationOutcome(
                new AcknowledgmentResponse
                {
                    ResponseCode = "103",
                    ResponseDesc = "Internal processing error",
                    ServiceStatus = "FAILED"
                },
                Rejection: null,
                transaction);
        }

        if (simulation is Simulation.FailAsync)
        {
            transaction.Status = "FAILED";
            transaction.FailureCode = "3011";
            transaction.FailureReason = "Transaction failed (simulated)";
            _store.Update(transaction);
        }

        if (simulation is Simulation.NoCallback)
        {
            // Accepted, but nothing will ever arrive — the stuck-saga case the recovery workers and
            // the requery path exist for.
            _logger.LogInformation(
                "Accepted {RequestId} but suppressing the callback (X-Simulate: no-callback)",
                transaction.RequestId);
        }
        else
        {
            _callbacks.ScheduleResult(transaction);
        }

        _logger.LogInformation(
            "Accepted Equity {Type}: RequestId={RequestId}, TransactionId={TransactionId}, Amount={Amount} {Currency}",
            transaction.Type, transaction.RequestId, transaction.TransactionId, amount, currency);

        return new SimulationOutcome(new AcknowledgmentResponse(), Rejection: null, transaction);
    }

    private enum Simulation
    {
        Succeed,
        FailAsync,
        RejectAtAck,
        NoCallback
    }

    /// <summary>
    /// Failure triggers, in priority order: the X-Simulate header, then amount conventions so that
    /// scripted flows can fail without setting headers.
    /// </summary>
    private static Simulation ResolveSimulation(PaymentSubmission submission, decimal amount) =>
        submission.SimulationHeader?.Trim().ToLowerInvariant() switch
        {
            "fail" or "fail-async" => Simulation.FailAsync,
            "reject" or "fail-ack" => Simulation.RejectAtAck,
            "no-callback" or "timeout" => Simulation.NoCallback,
            "success" => Simulation.Succeed,
            _ => amount switch
            {
                // Mirrors the amount-based conventions the other mocked providers in this repo use.
                13m => Simulation.FailAsync,
                14m => Simulation.RejectAtAck,
                15m => Simulation.NoCallback,
                _ => Simulation.Succeed
            }
        };

    private static bool IsSupportedCurrency(string currency) =>
        currency.ToUpperInvariant() is "KES" or "UGX" or "TZS" or "RWF" or "SDG" or "CDF" or "ETB";

    /// <summary>Equity ids look like DF3801U7FA — two letters, digits, a letter, digits.</summary>
    private static string GenerateTransactionId()
    {
        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var chars = new char[10];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = i is 0 or 1 or 6
                ? letters[Random.Shared.Next(letters.Length)]
                : (char)('0' + Random.Shared.Next(10));
        }

        return new string(chars);
    }

    private static SimulationOutcome Reject(string code, string message) =>
        new(
            Acknowledgment: null,
            Rejection: new RejectionResult(
                code == "101" ? StatusCodes.Status401Unauthorized : StatusCodes.Status400BadRequest,
                new GenericResponse
                {
                    Message = message,
                    Code = code,
                    Metadata = new Dictionary<string, string?>
                    {
                        ["status"] = "FAILED",
                        ["errorMessage"] = message
                    }
                }),
            Transaction: null);
}
