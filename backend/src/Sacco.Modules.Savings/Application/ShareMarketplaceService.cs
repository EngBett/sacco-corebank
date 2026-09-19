using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Savings.Domain;
using Sacco.Modules.Savings.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Auth;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Members;
using Sacco.Shared.Notifications;
using Sacco.Shared.Savings;
using Sacco.Shared.Tenancy;
using Sacco.Shared.Time;

namespace Sacco.Modules.Savings.Application;

/// <summary>
/// Member-to-member share capital marketplace (ADR 0008 follow-up): share capital can't be
/// withdrawn directly, only sold to another member. Transfers always happen at par (the seller's
/// own book value — no premium/discount) and are maker-checker: a member lists, a different member
/// claims it, and a staff member with <see cref="Permissions.Savings.SharesMarketplaceApprove"/> must
/// approve before shares and cash actually move. Both legs settle in one journal: shares move
/// seller→buyer within BOSA, cash moves buyer→seller within FOSA — neither leg crosses the
/// FOSA/BOSA boundary, so no inter-segment clearing pair is needed (ADR 0002).
/// </summary>
public sealed class ShareMarketplaceService(SavingsDbContext db, ILedgerService ledger, IMemberDirectory members, ITenantContext tenant, IClock clock, IAuditLogger audit, INotifier notifier)
{
    public async Task<ShareListing> ListAsync(Guid sellerMemberId, decimal amount, Guid byUser, CancellationToken ct)
    {
        await RequireGoodStanding(sellerMemberId, ct);
        var sharesAccount = await RequireActiveAccount(sellerMemberId, ProductKind.Shares, ct);
        var fosaAccount = await RequireActiveAccount(sellerMemberId, ProductKind.FosaCurrent, ct);

        var sharesProduct = await db.Products.FirstAsync(p => p.Kind == ProductKind.Shares, ct);
        var snapshot = await ledger.FindAccountAsync(sharesAccount.AccountNumber, ct) ?? throw new NotFoundException("Ledger account", sharesAccount.AccountNumber);
        if (snapshot.AvailableBalance - amount < sharesProduct.MinimumBalance)
            throw new DomainRuleException("savings.shares.listing.below_minimum", $"Listing this amount would take your share balance below the required minimum of {sharesProduct.MinimumBalance:N2}.");

        var listing = ShareListing.Create(Ids.New(), tenant.TenantId, sellerMemberId, sharesAccount.AccountNumber, fosaAccount.AccountNumber, amount, byUser, clock.UtcNow);
        await ledger.PlaceHoldAsync(sharesAccount.AccountNumber, amount, $"Share listing {listing.Id}", ct);
        db.ShareListings.Add(listing);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.shares.listing.created", nameof(ShareListing), listing.Id.ToString(), byUser, $$"""{"seller":"{{sellerMemberId}}","amount":{{amount}}}"""), ct);
        return listing;
    }

    public async Task<IReadOnlyList<ShareListing>> GetOpenListingsAsync(Guid excludingMemberId, CancellationToken ct)
        => await db.ShareListings.AsNoTracking().Where(l => l.Status == ShareListingStatus.Open && l.SellerMemberId != excludingMemberId).OrderBy(l => l.ListedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<ShareListing>> GetMyListingsAsync(Guid memberId, CancellationToken ct)
        => await db.ShareListings.AsNoTracking().Where(l => l.SellerMemberId == memberId || l.BuyerMemberId == memberId).OrderByDescending(l => l.ListedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<ShareListing>> ListAsync(ShareListingStatus? status, CancellationToken ct)
        => await db.ShareListings.AsNoTracking().Where(l => status == null || l.Status == status).OrderByDescending(l => l.ClaimedAt ?? l.ListedAt).Take(200).ToListAsync(ct);

    public async Task<IReadOnlyList<ShareListing>> GetPendingApprovalAsync(CancellationToken ct)
        => await db.ShareListings.AsNoTracking().Where(l => l.Status == ShareListingStatus.PendingApproval).OrderBy(l => l.ClaimedAt).ToListAsync(ct);

    public async Task<ShareListing> GetAsync(Guid id, CancellationToken ct)
        => await db.ShareListings.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Share listing", id);

    public async Task<ShareListing> ClaimAsync(Guid id, Guid buyerMemberId, Guid byUser, CancellationToken ct)
    {
        var listing = await GetAsync(id, ct);
        await RequireGoodStanding(buyerMemberId, ct);
        var buyerShares = await RequireActiveAccount(buyerMemberId, ProductKind.Shares, ct);
        var buyerFosa = await RequireActiveAccount(buyerMemberId, ProductKind.FosaCurrent, ct);

        var fosaProduct = await db.Products.FirstAsync(p => p.Kind == ProductKind.FosaCurrent, ct);
        var fosaSnapshot = await ledger.FindAccountAsync(buyerFosa.AccountNumber, ct) ?? throw new NotFoundException("Ledger account", buyerFosa.AccountNumber);
        if (fosaSnapshot.AvailableBalance - listing.Amount < fosaProduct.MinimumBalance)
            throw new InsufficientFundsException(buyerFosa.AccountNumber, listing.Amount);

        listing.Claim(buyerMemberId, buyerShares.AccountNumber, buyerFosa.AccountNumber, byUser, clock.UtcNow);
        await ledger.PlaceHoldAsync(buyerFosa.AccountNumber, listing.Amount, $"Share purchase {listing.Id}", ct);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.shares.listing.claimed", nameof(ShareListing), listing.Id.ToString(), byUser, $$"""{"buyer":"{{buyerMemberId}}","amount":{{listing.Amount}}}"""), ct);
        await notifier.NotifyAsync(new NotificationRequest("savings.shares.listing.claimed", $"Share sale of KES {listing.Amount:N2} awaits approval",
            $"{listing.SellerSharesAccountNumber} → {listing.BuyerSharesAccountNumber}", "/savings/shares-marketplace",
            NotificationAudience.HoldersOf(Permissions.Savings.SharesMarketplaceApprove), byUser), ct);
        return listing;
    }

    /// <summary>Checker approval posts one balanced journal: Dr seller shares / Cr buyer shares (BOSA), Dr buyer FOSA / Cr seller FOSA (cash settlement).</summary>
    public async Task<ShareListing> ApproveAsync(Guid id, Guid byUser, CancellationToken ct)
    {
        var listing = await GetAsync(id, ct);
        var sharesProduct = await db.Products.FirstAsync(p => p.Kind == ProductKind.Shares, ct);
        var fosaProduct = await db.Products.FirstAsync(p => p.Kind == ProductKind.FosaCurrent, ct);

        var reference = $"SHARE-XFER:{listing.Id}";
        var lines = new List<PostingLine>
        {
            new(sharesProduct.ControlGlAccountCode, Segment.Bosa, EntryDirection.Debit, listing.Amount, listing.SellerSharesAccountNumber, "Shares sold on marketplace"),
            new(sharesProduct.ControlGlAccountCode, Segment.Bosa, EntryDirection.Credit, listing.Amount, listing.BuyerSharesAccountNumber, "Shares bought on marketplace"),
            new(fosaProduct.ControlGlAccountCode, Segment.Fosa, EntryDirection.Debit, listing.Amount, listing.BuyerFosaAccountNumber, "Payment for purchased shares"),
            new(fosaProduct.ControlGlAccountCode, Segment.Fosa, EntryDirection.Credit, listing.Amount, listing.SellerFosaAccountNumber, "Proceeds from share sale"),
        };
        await ledger.PostAsync(new PostingRequest(reference, $"Share transfer — listing {listing.Id}", clock.Today, "Savings", byUser, lines), ct);
        await ledger.ReleaseHoldAsync(listing.SellerSharesAccountNumber, listing.Amount, $"Share listing {listing.Id} approved", ct);
        await ledger.ReleaseHoldAsync(listing.BuyerFosaAccountNumber!, listing.Amount, $"Share listing {listing.Id} approved", ct);
        listing.Approve(byUser, reference, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.shares.listing.approved", nameof(ShareListing), id.ToString(), byUser, $$"""{"amount":{{listing.Amount}}}"""), ct);
        await notifier.NotifyAsync(new NotificationRequest("savings.shares.listing.approved", "Share transfer approved", $"KES {listing.Amount:N2} of shares changed hands.",
            "/savings/shares-marketplace", NotificationAudience.Users(listing.ListedByUserId, listing.ClaimedByUserId!.Value), byUser), ct);
        return listing;
    }

    public async Task<ShareListing> RejectAsync(Guid id, string reason, Guid byUser, CancellationToken ct)
    {
        var listing = await GetAsync(id, ct);
        listing.Reject(byUser, reason, clock.UtcNow);
        await ledger.ReleaseHoldAsync(listing.SellerSharesAccountNumber, listing.Amount, $"Share listing {listing.Id} rejected", ct);
        await ledger.ReleaseHoldAsync(listing.BuyerFosaAccountNumber!, listing.Amount, $"Share listing {listing.Id} rejected", ct);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.shares.listing.rejected", nameof(ShareListing), id.ToString(), byUser, $$"""{"reason":"{{reason.Replace("\"", "'")}}"}"""), ct);
        await notifier.NotifyAsync(new NotificationRequest("savings.shares.listing.rejected", "Share transfer rejected", reason,
            "/savings/shares-marketplace", NotificationAudience.Users(listing.ListedByUserId, listing.ClaimedByUserId!.Value), byUser), ct);
        return listing;
    }

    public async Task<ShareListing> CancelAsync(Guid id, Guid sellerMemberId, CancellationToken ct)
    {
        var listing = await GetAsync(id, ct);
        listing.Cancel(sellerMemberId);
        await ledger.ReleaseHoldAsync(listing.SellerSharesAccountNumber, listing.Amount, $"Share listing {listing.Id} cancelled", ct);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEvent("savings.shares.listing.cancelled", nameof(ShareListing), id.ToString(), sellerMemberId, "{}"), ct);
        return listing;
    }

    private async Task<SavingsAccount> RequireActiveAccount(Guid memberId, ProductKind kind, CancellationToken ct)
        => await db.Accounts.FirstOrDefaultAsync(a => a.MemberId == memberId && a.Kind == kind && a.Status == SavingsAccountStatus.Active, ct)
           ?? throw new DomainRuleException($"savings.shares.listing.no_{kind.ToString().ToLowerInvariant()}_account", $"An active {kind} account is required to trade on the shares marketplace.");

    private async Task<MemberSummary> RequireGoodStanding(Guid memberId, CancellationToken ct)
    {
        var member = await members.FindAsync(memberId, ct) ?? throw new NotFoundException("Member", memberId);
        if (member.KycStatus != KycStatus.Verified)
            throw new DomainRuleException("savings.member_not_in_good_standing", $"Member {member.MemberNumber} is {member.KycStatus}; only KYC-verified members in good standing can trade shares.");
        return member;
    }
}
