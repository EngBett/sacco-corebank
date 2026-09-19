using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Platform.Domain;

/// <summary>
/// A SACCO office: head office, a branch or a satellite/agency outlet (ADR 0018). Branches are a dimension on people and
/// postings — which office registered a member, where a staff member works, which office a journal was raised at — not a
/// separate set of books: the ledger stays one chart of accounts per tenant (ADR 0002).
/// </summary>
public class Branch : TenantEntity
{
    private Branch() { }

    /// <summary>Short code used in listings and reports, e.g. "HQ", "NKR". Unique per SACCO.</summary>
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    /// <summary>Exactly one branch is the head office; it is the fallback when nothing else names a branch.</summary>
    public bool IsHeadOffice { get; private set; }
    public string? County { get; private set; }
    public string? Town { get; private set; }
    public string? PhysicalAddress { get; private set; }
    public string? PhoneNumber { get; private set; }
    public string? Email { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }

    public static Branch Create(Guid id, Guid tenantId, string code, string name, bool isHeadOffice, DateTimeOffset now)
    {
        var branch = new Branch { Id = id, TenantId = tenantId, IsActive = true, OpenedAt = now };
        branch.Update(code, name, isHeadOffice, null, null, null, null, null, 0, isActive: true);
        return branch;
    }

    public void Update(string code, string name, bool isHeadOffice, string? county, string? town, string? physicalAddress,
        string? phoneNumber, string? email, int displayOrder, bool isActive)
    {
        code = code.Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 10 || code.Any(c => !char.IsLetterOrDigit(c) && c != '-'))
            throw new DomainRuleException("platform.branch.code_invalid", "Branch code must be 2–10 letters, digits or hyphens.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 150)
            throw new DomainRuleException("platform.branch.name_required", "Branch name is required (150 characters or fewer).");
        if (!isActive && isHeadOffice)
            throw new DomainRuleException("platform.branch.head_office_active", "The head office cannot be closed. Make another branch the head office first.");
        if (displayOrder is < 0 or > 10_000)
            throw new DomainRuleException("platform.branch.order_invalid", "Display order must be between 0 and 10000.");

        Code = code;
        Name = name.Trim();
        IsHeadOffice = isHeadOffice;
        County = Clean(county, 100);
        Town = Clean(town, 100);
        PhysicalAddress = Clean(physicalAddress, 300);
        PhoneNumber = Clean(phoneNumber, 20);
        Email = Clean(email, 200);
        DisplayOrder = displayOrder;
        IsActive = isActive;
    }

    /// <summary>Head office moves are exclusive: the service demotes the previous one in the same transaction.</summary>
    public void SetHeadOffice(bool isHeadOffice) => IsHeadOffice = isHeadOffice;

    private static string? Clean(string? value, int maxLength)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (clean is not null && clean.Length > maxLength)
            throw new DomainRuleException("platform.branch.field_too_long", $"Keep branch details to {maxLength} characters or fewer.");
        return clean;
    }
}
