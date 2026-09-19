using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Platform.Domain;

/// <summary>
/// A member service a SACCO advertises on its public website (mobile banking, ATM cards, standing orders…). Content,
/// not configuration of how money moves — each SACCO lists what it actually offers.
/// </summary>
public class PublicService : TenantEntity
{
    /// <summary>Icon keys the websites know how to draw; anything else would render as a blank.</summary>
    public static readonly IReadOnlyList<string> Icons =
        ["mobile", "sms", "atm", "mpesa", "standing-order", "cheque", "salary", "safe-custody", "insurance", "support", "diaspora", "agency", "general"];

    private PublicService() { }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Icon { get; private set; } = "general";
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }

    public static PublicService Create(Guid id, Guid tenantId, string name, string? description, string icon, int displayOrder)
    {
        var service = new PublicService { Id = id, TenantId = tenantId, IsActive = true };
        service.Update(name, description, icon, displayOrder, isActive: true);
        return service;
    }

    public void Update(string name, string? description, string icon, int displayOrder, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            throw new DomainRuleException("platform.service.name_invalid", "Service name is required (100 characters or fewer).");
        if (description is { Length: > 300 })
            throw new DomainRuleException("platform.service.description_too_long", "Keep the description to 300 characters or fewer.");
        if (!Icons.Contains(icon))
            throw new DomainRuleException("platform.service.icon_invalid", $"Icon must be one of: {string.Join(", ", Icons)}.");
        if (displayOrder is < 0 or > 10_000)
            throw new DomainRuleException("platform.service.order_invalid", "Display order must be between 0 and 10000.");
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Icon = icon;
        DisplayOrder = displayOrder;
        IsActive = isActive;
    }
}
