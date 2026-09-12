using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Ledger.Domain;

public enum GlAccountCategory
{
    Asset = 1,
    Liability = 2,
    Equity = 3,
    Income = 4,
    Expense = 5,
}

/// <summary>
/// A node in the chart of accounts. Every GL account carries an explicit FOSA/BOSA segment
/// (ADR 0002). <see cref="Balance"/> is a running balance maintained by the posting engine via
/// atomic UPDATEs and is reconciled against the sum of journal lines by the reconciliation query.
/// </summary>
public class GlAccount : TenantEntity
{
    private GlAccount() { }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public GlAccountCategory Category { get; private set; }
    public Segment Segment { get; private set; }
    /// <summary>Which side increases the balance. Defaults from category; contra accounts override (e.g. loan-loss provision).</summary>
    public EntryDirection NormalBalance { get; private set; }
    public Guid? ParentId { get; private set; }
    /// <summary>Leaf accounts accept postings; header accounts only aggregate.</summary>
    public bool IsPostable { get; private set; }
    /// <summary>Control accounts have member sub-ledger accounts hanging off them; their balance equals the sum of those sub-accounts.</summary>
    public bool IsControlAccount { get; private set; }
    public bool IsActive { get; private set; }
    /// <summary>Signed toward the normal side: positive means "balance on the normal side".</summary>
    public decimal Balance { get; private set; }
    public string? Description { get; private set; }

    public static EntryDirection DefaultNormalBalance(GlAccountCategory category) => category switch
    {
        GlAccountCategory.Asset or GlAccountCategory.Expense => EntryDirection.Debit,
        _ => EntryDirection.Credit,
    };

    public static GlAccount Create(Guid id, Guid tenantId, string code, string name, GlAccountCategory category, Segment segment,
        bool isPostable, bool isControlAccount, Guid? parentId, string? description, EntryDirection? normalBalance = null)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new DomainRuleException("ledger.gl_account.code_required", "GL account code is required.");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainRuleException("ledger.gl_account.name_required", "GL account name is required.");
        if (isControlAccount && !isPostable) throw new DomainRuleException("ledger.gl_account.control_must_be_postable", "A control account must be postable.");
        return new GlAccount
        {
            Id = id,
            TenantId = tenantId,
            Code = code.Trim(),
            Name = name.Trim(),
            Category = category,
            Segment = segment,
            NormalBalance = normalBalance ?? DefaultNormalBalance(category),
            ParentId = parentId,
            IsPostable = isPostable,
            IsControlAccount = isControlAccount,
            IsActive = true,
            Balance = 0m,
            Description = description,
        };
    }

    public void Rename(string name, string? description) { Name = name; Description = description; }
    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;

    /// <summary>Signed delta to apply to <see cref="Balance"/> for a line of the given direction.</summary>
    public decimal DeltaFor(EntryDirection direction, decimal amount) => direction == NormalBalance ? amount : -amount;
}
