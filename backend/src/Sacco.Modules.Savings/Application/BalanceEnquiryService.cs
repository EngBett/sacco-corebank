using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sacco.Modules.Savings.Domain;
using Sacco.Modules.Savings.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Savings;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Savings.Application;

/// <param name="Fee">The enquiry fee the account carries; null when its balance is free to view.</param>
/// <param name="VisibleUntil">End of the paid reveal window, when one is open.</param>
public sealed record BalanceVisibility(bool Locked, decimal? Fee, DateTimeOffset? VisibleUntil);

public sealed record BalanceRevealResult(bool Charged, decimal Fee, DateTimeOffset? VisibleUntil);

/// <summary>
/// Balance-enquiry fees (ADR 0015). A product with an active balance-enquiry rule keeps its balances hidden from
/// self-service until the member pays for a short reveal window. The fee always comes off the member's FOSA account —
/// BOSA deposits and share capital aren't withdrawable, so they are never debited for it. Staff views are never gated.
/// </summary>
public sealed class BalanceEnquiryService(SavingsDbContext db, ILedgerService ledger, ITenantContext tenant, IClock clock, IAuditLogger audit, IOptions<SavingsSettings> options) : IBalanceVisibility
{
    private SavingsSettings Settings => options.Value;

    public async Task<IReadOnlyDictionary<string, BalanceVisibility>> VisibilityAsync(Guid memberId, IReadOnlyCollection<SavingsAccount> accounts, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var gates = await ActiveGatesAsync(ct);
        var open = await db.BalanceEnquiries.AsNoTracking()
            .Where(e => e.MemberId == memberId && e.VisibleUntil > now)
            .GroupBy(e => e.AccountNumber).Select(g => new { g.Key, Until = g.Max(e => e.VisibleUntil) })
            .ToDictionaryAsync(x => x.Key, x => x.Until, ct);
        return accounts.ToDictionary(a => a.AccountNumber, a =>
        {
            if (!gates.TryGetValue(a.ProductCode, out var rule)) return new BalanceVisibility(false, null, null);
            return open.TryGetValue(a.AccountNumber, out var until)
                ? new BalanceVisibility(false, rule.FixedAmount, until)
                : new BalanceVisibility(true, rule.FixedAmount, null);
        });
    }

    public async Task<bool> IsBalanceVisibleAsync(Guid memberId, string accountNumber, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountNumber == accountNumber && a.MemberId == memberId, ct);
        if (account is null) return true; // not a savings account (e.g. a loan) — nothing to gate
        return !(await VisibilityAsync(memberId, [account], ct))[accountNumber].Locked;
    }

    public async Task<BalanceRevealResult> RevealAsync(Guid memberId, string accountNumber, string idempotencyKey, Guid byUser, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 64)
            throw new DomainRuleException("savings.balance_enquiry.key_invalid", "An idempotency key (up to 64 characters) is required.");
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountNumber == accountNumber && a.MemberId == memberId, ct)
                      ?? throw new NotFoundException("Account", accountNumber);

        var replay = await db.BalanceEnquiries.AsNoTracking().FirstOrDefaultAsync(e => e.MemberId == memberId && e.IdempotencyKey == idempotencyKey, ct);
        if (replay is not null) return new BalanceRevealResult(false, replay.Fee, replay.VisibleUntil);

        var visibility = (await VisibilityAsync(memberId, [account], ct))[accountNumber];
        if (!visibility.Locked) return new BalanceRevealResult(false, 0m, visibility.VisibleUntil);

        var rule = (await ActiveGatesAsync(ct))[account.ProductCode];
        var fosa = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.MemberId == memberId && a.Kind == ProductKind.FosaCurrent && a.Status == SavingsAccountStatus.Active, ct)
                   ?? throw new DomainRuleException("savings.balance_enquiry.no_fosa", "A balance enquiry is charged to your FOSA account, and you don't have an active one.");
        var fosaProduct = await db.Products.AsNoTracking().FirstAsync(p => p.Id == fosa.ProductId, ct);
        var fee = rule.Calculate(0m);
        var snapshot = await ledger.FindAccountAsync(fosa.AccountNumber, ct) ?? throw new NotFoundException("Ledger account", fosa.AccountNumber);
        if (snapshot.AvailableBalance - fee < fosaProduct.MinimumBalance)
            throw new InsufficientFundsException(fosa.AccountNumber, fee);

        var id = Ids.New();
        var reference = $"BAL-ENQ:{id:N}";
        if (fee > 0)
        {
            var lines = new List<PostingLine> { new(fosaProduct.ControlGlAccountCode, fosa.Segment, EntryDirection.Debit, fee, fosa.AccountNumber, $"Balance enquiry — {accountNumber}") };
            lines.AddRange(PostingBuilder.Fee(Settings, fosa.Segment, fee, rule.FeeIncomeGlAccountCode, rule.FeeIncomeSegment, "Balance enquiry fee"));
            await ledger.PostAsync(new PostingRequest(reference, $"Balance enquiry fee — {accountNumber}", clock.Today, "Savings", byUser, lines), ct);
        }

        var enquiry = BalanceEnquiry.Record(id, tenant.TenantId, memberId, accountNumber, idempotencyKey, fee, rule.Id, fosa.AccountNumber, fee > 0 ? reference : "", byUser, clock.UtcNow,
            TimeSpan.FromMinutes(Settings.BalanceRevealMinutes));
        db.BalanceEnquiries.Add(enquiry);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.balance_enquiry.charged", nameof(BalanceEnquiry), id.ToString(), byUser,
            $$"""{"account":"{{accountNumber}}","fee":{{fee}},"chargedTo":"{{fosa.AccountNumber}}"}"""), ct);
        return new BalanceRevealResult(true, fee, enquiry.VisibleUntil);
    }

    private async Task<Dictionary<string, FeeRule>> ActiveGatesAsync(CancellationToken ct)
        => (await db.FeeRules.AsNoTracking().Where(r => r.Status == FeeRuleStatus.Active && r.TransactionType == FeeTransactionType.BalanceEnquiry && r.ProductCode != null).ToListAsync(ct))
            .GroupBy(r => r.ProductCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.DecidedAt).First(), StringComparer.OrdinalIgnoreCase);
}
