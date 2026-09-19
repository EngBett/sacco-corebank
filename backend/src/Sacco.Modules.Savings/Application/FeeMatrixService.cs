using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Savings.Domain;
using Sacco.Modules.Savings.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Notifications;
using Sacco.Shared.Savings;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Savings.Application;

public sealed record FeeTierInput(decimal? UpTo, decimal Charge);
public sealed record CreateFeeRuleCommand(FeeTransactionType TransactionType, FeeChannel? Channel, string? ProductCode, decimal MinAmount, decimal? MaxAmount,
    FeeChargeType ChargeType, decimal? FixedAmount, int? RateBps, decimal? MinCharge, decimal? MaxCharge, IReadOnlyList<FeeTierInput>? Tiers,
    string FeeIncomeGlAccountCode, Guid? SupersedesRuleId);

/// <summary>
/// The fee matrix (ADR 0015): maintaining rules (maker-checker) and pricing a transaction against them. When no active
/// rule matches, withdrawals fall back to the product's own withdrawal fee and deposits/balance enquiries are free.
/// </summary>
public sealed class FeeMatrixService(SavingsDbContext db, ILedgerService ledger, ITenantContext tenant, IClock clock, IAuditLogger audit, INotifier notifier)
{
    public async Task<IReadOnlyList<FeeRule>> ListAsync(FeeRuleStatus? status, FeeTransactionType? type, CancellationToken ct)
    {
        var q = db.FeeRules.AsNoTracking();
        if (status is { } s) q = q.Where(r => r.Status == s);
        if (type is { } t) q = q.Where(r => r.TransactionType == t);
        return await q.OrderBy(r => r.TransactionType).ThenBy(r => r.Channel).ThenBy(r => r.ProductCode).ThenBy(r => r.MinAmount).ThenByDescending(r => r.CreatedAt).ToListAsync(ct);
    }

    public async Task<FeeRule> GetAsync(Guid id, CancellationToken ct)
        => await db.FeeRules.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Fee rule", id);

    public async Task<FeeRule> CreateAsync(CreateFeeRuleCommand c, Guid byUser, CancellationToken ct)
    {
        if (c.ProductCode is { Length: > 0 } code && !await db.Products.AnyAsync(p => p.Code == code.Trim().ToUpperInvariant(), ct))
            throw new DomainRuleException("savings.fees.product_unknown", $"There is no savings product {code}.");
        var gl = await ledger.FindGlAccountAsync(c.FeeIncomeGlAccountCode?.Trim() ?? "", ct);
        if (gl is null || !gl.IsActive || !gl.IsPostable || gl.IsControlAccount || gl.Category != "Income")
            throw new DomainRuleException("savings.fees.gl_not_income", $"GL {c.FeeIncomeGlAccountCode} must be an active, postable income account (not a control account).");
        if (c.SupersedesRuleId is { } supersedes && !await db.FeeRules.AnyAsync(r => r.Id == supersedes && r.Status == FeeRuleStatus.Active, ct))
            throw new DomainRuleException("savings.fees.supersedes_not_active", "Only an active rule can be revised.");

        var rule = FeeRule.Create(Ids.New(), tenant.TenantId, c.TransactionType, c.Channel, c.ProductCode, c.MinAmount, c.MaxAmount, c.ChargeType, c.FixedAmount, c.RateBps,
            c.MinCharge, c.MaxCharge, (c.Tiers ?? []).Select(t => (t.UpTo, t.Charge)).ToList(), gl.Code, gl.Segment, c.SupersedesRuleId, byUser, clock.UtcNow);
        db.FeeRules.Add(rule);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.fees.rule_created", nameof(FeeRule), rule.Id.ToString(), byUser, Describe(rule)), ct);
        await notifier.NotifyAsync(new NotificationRequest("savings.fees.pending", $"Fee rule awaits approval: {Label(rule)}", rule.Describe(), "/savings/fees",
            NotificationAudience.HoldersOf(Permissions.Savings.FeesApprove), byUser), ct);
        return rule;
    }

    public async Task<FeeRule> ApproveAsync(Guid id, Guid byUser, CancellationToken ct)
    {
        var rule = await GetAsync(id, ct);
        rule.Approve(byUser, clock.UtcNow);

        var active = await db.FeeRules.Where(r => r.Status == FeeRuleStatus.Active && r.TransactionType == rule.TransactionType && r.Id != rule.SupersedesRuleId).ToListAsync(ct);
        if (active.FirstOrDefault(rule.Overlaps) is { } clash)
            throw new DomainRuleException("savings.fees.overlap", $"An active rule already prices this type, channel and product over an overlapping amount range ({clash.MinAmount:N0}–{(clash.MaxAmount is { } m ? m.ToString("N0") : "∞")}). Revise that rule instead.");
        if (rule.SupersedesRuleId is { } old)
            (await GetAsync(old, ct)).Deactivate(byUser, clock.UtcNow);

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.fees.rule_approved", nameof(FeeRule), id.ToString(), byUser, $$"""{"createdBy":"{{rule.CreatedByUserId}}","supersedes":"{{rule.SupersedesRuleId}}"}"""), ct);
        await notifier.NotifyAsync(new NotificationRequest("savings.fees.approved", $"Fee rule is live: {Label(rule)}", rule.Describe(), "/savings/fees", NotificationAudience.User(rule.CreatedByUserId), byUser), ct);
        return rule;
    }

    public async Task<FeeRule> RejectAsync(Guid id, string reason, Guid byUser, CancellationToken ct)
    {
        var rule = await GetAsync(id, ct);
        rule.Reject(byUser, reason, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.fees.rule_rejected", nameof(FeeRule), id.ToString(), byUser, $$"""{"reason":"{{reason.Replace("\"", "'")}}"}"""), ct);
        await notifier.NotifyAsync(new NotificationRequest("savings.fees.rejected", $"Fee rule rejected: {Label(rule)}", reason, "/savings/fees", NotificationAudience.User(rule.CreatedByUserId), byUser), ct);
        return rule;
    }

    public async Task<FeeRule> DeactivateAsync(Guid id, Guid byUser, CancellationToken ct)
    {
        var rule = await GetAsync(id, ct);
        rule.Deactivate(byUser, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.fees.rule_deactivated", nameof(FeeRule), id.ToString(), byUser, Describe(rule)), ct);
        return rule;
    }

    /// <summary>Prices a transaction: the most specific active rule wins; otherwise the product default (withdrawals) or no fee.</summary>
    public async Task<FeeCharge> QuoteAsync(FeeTransactionType type, FeeChannel? channel, SavingsProduct product, decimal amount, CancellationToken ct)
    {
        var rules = await db.FeeRules.AsNoTracking().Where(r => r.Status == FeeRuleStatus.Active && r.TransactionType == type).ToListAsync(ct);
        var best = rules.Where(r => r.Applies(type, channel, product.Code, amount))
            .OrderByDescending(r => r.Precedence.Specificity).ThenBy(r => r.Precedence.Width).ThenByDescending(r => r.DecidedAt)
            .FirstOrDefault();
        if (best is not null) return best.ToCharge(amount);
        return type == FeeTransactionType.Withdrawal ? FeeCharge.ProductDefault(product) : FeeCharge.None(product.Segment);
    }

    public static FeeChannel ChannelFor(PayoutChannel channel) => channel switch
    {
        PayoutChannel.Cash => FeeChannel.Cash,
        PayoutChannel.MPesa => FeeChannel.MPesa,
        PayoutChannel.AirtelMoney => FeeChannel.AirtelMoney,
        _ => FeeChannel.BankTransfer,
    };

    /// <summary>Null for internal transfers — they are never charged.</summary>
    public static FeeChannel? ChannelFor(DepositChannel channel) => channel switch
    {
        DepositChannel.Cash => FeeChannel.Cash,
        DepositChannel.MPesa => FeeChannel.MPesa,
        DepositChannel.AirtelMoney => FeeChannel.AirtelMoney,
        DepositChannel.BankTransfer => FeeChannel.BankTransfer,
        DepositChannel.CheckOff => FeeChannel.CheckOff,
        _ => null,
    };

    private static string Label(FeeRule r) => $"{r.TransactionType}{(r.Channel is { } c ? $" · {c}" : "")}{(r.ProductCode is { } p ? $" · {p}" : "")}";

    private static string Describe(FeeRule r) =>
        $$"""{"type":"{{r.TransactionType}}","channel":"{{r.Channel}}","product":"{{r.ProductCode}}","min":{{r.MinAmount}},"max":{{(r.MaxAmount?.ToString() ?? "null")}},"charge":"{{r.Describe()}}","gl":"{{r.FeeIncomeGlAccountCode}}"}""";
}
