using Sacco.Shared.Domain;
using Sacco.Shared.Savings;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Savings.Domain;

public enum SavingsAccountStatus { Active = 1, Matured = 2, Closed = 3 }

/// <summary>
/// Product-level view of a member's account. The balance lives in the Ledger module's
/// sub-ledger account with the same number; this row carries product/term state.
/// </summary>
public class SavingsAccount : TenantEntity
{
    private SavingsAccount() { }

    public string AccountNumber { get; private set; } = string.Empty;
    public Guid MemberId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductCode { get; private set; } = string.Empty;
    public ProductKind Kind { get; private set; }
    public Segment Segment { get; private set; }
    public SavingsAccountStatus Status { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public Guid OpenedByUserId { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    // Fixed-deposit terms
    public decimal? Principal { get; private set; }
    public int? TermMonths { get; private set; }
    public int? InterestRateBps { get; private set; }
    public DateOnly? MaturityDate { get; private set; }
    public string? PayoutAccountNumber { get; private set; }

    public static SavingsAccount Open(Guid id, Guid tenantId, string accountNumber, Guid memberId, SavingsProduct product, Guid openedBy, DateTimeOffset now)
        => new()
        {
            Id = id, TenantId = tenantId, AccountNumber = accountNumber, MemberId = memberId, ProductId = product.Id, ProductCode = product.Code,
            Kind = product.Kind, Segment = product.Segment, Status = SavingsAccountStatus.Active, OpenedAt = now, OpenedByUserId = openedBy,
        };

    public static SavingsAccount OpenFixedDeposit(Guid id, Guid tenantId, string accountNumber, Guid memberId, SavingsProduct product, decimal principal, string payoutAccountNumber, Guid openedBy, DateTimeOffset now, DateOnly today)
    {
        if (product.Kind != ProductKind.FixedDeposit) throw new DomainRuleException("savings.fd.not_fd_product", $"{product.Code} is not a fixed deposit product.");
        if (principal < product.MinimumOpeningDeposit) throw new DomainRuleException("savings.fd.below_minimum", $"Minimum fixed deposit for {product.Code} is {product.MinimumOpeningDeposit:N2}.");
        var a = Open(id, tenantId, accountNumber, memberId, product, openedBy, now);
        a.Principal = principal; a.TermMonths = product.TermMonths; a.InterestRateBps = product.InterestRateBps;
        a.MaturityDate = today.AddMonths(product.TermMonths!.Value); a.PayoutAccountNumber = payoutAccountNumber;
        return a;
    }

    public decimal MaturityInterest() =>
        Principal is decimal p && TermMonths is int m && InterestRateBps is int r ? decimal.Round(p * r / 10_000m * m / 12m, 2, MidpointRounding.ToEven) : 0m;

    public void Mature(DateOnly today)
    {
        if (Kind != ProductKind.FixedDeposit) throw new DomainRuleException("savings.fd.not_fd", "Only fixed deposits mature.");
        if (Status != SavingsAccountStatus.Active) throw new DomainRuleException("savings.fd.not_active", $"Fixed deposit {AccountNumber} is {Status}.");
        if (MaturityDate is DateOnly md && today < md) throw new DomainRuleException("savings.fd.not_yet_matured", $"Fixed deposit {AccountNumber} matures on {md:yyyy-MM-dd}.");
        Status = SavingsAccountStatus.Matured;
    }

    public void Close(DateTimeOffset now)
    {
        if (Status == SavingsAccountStatus.Closed) throw new DomainRuleException("savings.account.closed", $"{AccountNumber} is already closed.");
        Status = SavingsAccountStatus.Closed; ClosedAt = now;
    }
}
