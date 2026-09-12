namespace Sacco.Shared.Domain;

/// <summary>
/// Base for exceptions that represent a business-rule outcome (not a programming error).
/// The API maps these to ProblemDetails with the matching HTTP status.
/// </summary>
public abstract class DomainException(string code, string message) : Exception(message)
{
    /// <summary>Stable machine-readable code, e.g. "ledger.unbalanced_entry".</summary>
    public string Code { get; } = code;
}

/// <summary>A business rule was violated (HTTP 422).</summary>
public class DomainRuleException(string code, string message) : DomainException(code, message);

/// <summary>The referenced entity does not exist in the current tenant (HTTP 404).</summary>
public class NotFoundException(string entity, object key)
    : DomainException("not_found", $"{entity} '{key}' was not found.")
{
    public string Entity { get; } = entity;
    public object Key { get; } = key;
}

/// <summary>An operation conflicts with existing state, e.g. a duplicate reference (HTTP 409).</summary>
public class ConflictException(string code, string message) : DomainException(code, message);

/// <summary>A debit would take an account below its available balance (HTTP 422).</summary>
public class InsufficientFundsException(string accountNumber, decimal requested)
    : DomainException("ledger.insufficient_funds",
        $"Account {accountNumber} has insufficient available balance for a debit of {requested:N2}.")
{
    public string AccountNumber { get; } = accountNumber;
    public decimal Requested { get; } = requested;
}

/// <summary>
/// The same user attempted to both initiate and approve a money-moving action
/// (segregation of duties, non-negotiable #6). HTTP 403.
/// </summary>
public class MakerCheckerViolationException(string action)
    : DomainException("maker_checker.same_user",
        $"The user who initiated '{action}' cannot also approve it. A different approver is required.")
{
    public string Action { get; } = action;
}

/// <summary>The caller lacks a required permission (HTTP 403).</summary>
public class ForbiddenException(string message) : DomainException("forbidden", message);
