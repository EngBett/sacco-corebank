using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sacco.Modules.Ledger.Domain;
using Sacco.Modules.Ledger.Persistence;
using Sacco.Seed.Data;
using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Time;

namespace Sacco.Seed.Seeders;

/// <summary>
/// Opens member sub-ledger accounts and posts a realistic, balanced transaction history for the
/// Demo SACCO: share purchases, monthly BOSA contributions, FOSA deposits/withdrawals, fees,
/// bank charges, teller cash banking, and an inter-segment transfer. Also leaves one manual
/// journal pending approval so the maker-checker flow can be demoed. Idempotent by journal reference.
/// </summary>
public sealed class LedgerSeeder(LedgerDbContext db, ILedgerService ledger, Sacco.Modules.Savings.Application.SavingsService savings, IClock clock, ILogger<LedgerSeeder> logger) : ISeeder
{
    public int Order => 20;

    public static string SavingsAccount(string memberNo) => $"{memberNo}-SV";
    public static string SharesAccount(string memberNo) => $"{memberNo}-SH";
    public static string FosaAccount(string memberNo) => $"{memberNo}-FO";
    public const string ProductBosaSavings = "BOSA-DEP";
    public const string ProductShares = "SHARES";
    public const string ProductFosaCurrent = "FOSA-CUR";

    public async Task SeedAsync(CancellationToken ct)
    {
        var existingAccounts = await db.LedgerAccounts.Select(a => a.AccountNumber).ToHashSetAsync(ct);
        var opened = 0;
        foreach (var m in KenyanNames.Members.Where(m => m.HasAccounts))
        {
            var memberId = DemoTenant.MemberId(m.MemberNumber);
            opened += await OpenIfMissing(existingAccounts, SharesAccount(m.MemberNumber), memberId, Coa.ShareCapitalControl, Segment.Bosa, LedgerAccountKind.Shares, ProductShares, ct);
            opened += await OpenIfMissing(existingAccounts, SavingsAccount(m.MemberNumber), memberId, Coa.BosaMemberDepositsControl, Segment.Bosa, LedgerAccountKind.Savings, ProductBosaSavings, ct);
            opened += await OpenIfMissing(existingAccounts, FosaAccount(m.MemberNumber), memberId, Coa.FosaSavingsControl, Segment.Fosa, LedgerAccountKind.Current, ProductFosaCurrent, ct);
        }
        logger.Created("Ledger accounts", opened);

        var existingRefs = await db.JournalEntries.Select(e => e.Reference).ToHashSetAsync(ct);
        var posted = 0;
        var today = clock.Today;
        var start = today.AddMonths(-12);
        var system = DemoTenant.Users.System;
        var teller = DemoTenant.Users.Teller;

        // 1. Institutional capital and opening bank balances (BOSA) — dated 12 months ago.
        posted += await PostIfMissing(existingRefs, new PostingRequest("OPEN-CAP-2025", "Opening institutional capital", start, "Seed", system,
        [
            new(Coa.BosaCashAtBank, Segment.Bosa, EntryDirection.Debit, 2_500_000m, Narrative: "Opening balance"),
            new(Coa.InstitutionalCapital, Segment.Bosa, EntryDirection.Credit, 2_500_000m, Narrative: "Opening balance"),
        ]), ct);
        posted += await PostIfMissing(existingRefs, new PostingRequest("OPEN-FOSA-2025", "FOSA float transferred from BOSA", start, "Seed", system,
        [
            // BOSA side: lend float to FOSA
            new(Coa.BosaDueFromFosa, Segment.Bosa, EntryDirection.Debit, 800_000m, Narrative: "Float to FOSA"),
            new(Coa.BosaCashAtBank, Segment.Bosa, EntryDirection.Credit, 800_000m, Narrative: "Float to FOSA"),
            // FOSA side: receive float
            new(Coa.FosaCashAtBank, Segment.Fosa, EntryDirection.Debit, 600_000m, Narrative: "Float from BOSA"),
            new(Coa.FosaCashOnHand, Segment.Fosa, EntryDirection.Debit, 200_000m, Narrative: "Teller float from BOSA"),
            new(Coa.FosaDueToBosa, Segment.Fosa, EntryDirection.Credit, 800_000m, Narrative: "Float from BOSA"),
        ]), ct);

        // 2. Per member: entrance fee + share purchase, then monthly BOSA contributions and FOSA activity.
        var rng = new Random(20260911);
        var withAccounts = KenyanNames.Members.Where(m => m.HasAccounts).ToList();
        for (var i = 0; i < withAccounts.Count; i++)
        {
            var m = withAccounts[i];
            var joinDate = start.AddDays(rng.Next(0, 60));
            var shares = 10_000m + 5_000m * rng.Next(0, 6);       // KES 10k–35k
            var monthly = 2_000m + 500m * rng.Next(0, 9);          // KES 2k–6k monthly contribution
            var fosaDeposit = 5_000m + 1_000m * rng.Next(0, 20);   // KES 5k–24k

            posted += await PostIfMissing(existingRefs, new PostingRequest($"JOIN:{m.MemberNumber}", $"Membership: entrance fee and share purchase — {m.FirstName} {m.LastName}", joinDate, "Seed", teller,
            [
                new(Coa.BosaCashAtBank, Segment.Bosa, EntryDirection.Debit, shares + 1_000m, Narrative: "Bank deposit slip"),
                new(Coa.ShareCapitalControl, Segment.Bosa, EntryDirection.Credit, shares, SharesAccount(m.MemberNumber), "Share purchase"),
                new(Coa.BosaEntranceFees, Segment.Bosa, EntryDirection.Credit, 1_000m, Narrative: "Entrance fee"),
            ]), ct);

            var monthCursor = new DateOnly(joinDate.Year, joinDate.Month, 1).AddMonths(1);
            var n = 0;
            while (monthCursor <= today && n < 12)
            {
                var valueDate = monthCursor.AddDays(Math.Min(27, 4 + rng.Next(0, 6)));
                if (valueDate > today) break;
                posted += await PostIfMissing(existingRefs, new PostingRequest($"CONTRIB:{m.MemberNumber}:{monthCursor:yyyy-MM}", $"Monthly deposit contribution — {m.FirstName} {m.LastName}", valueDate, "Seed", teller,
                [
                    new(Coa.BosaCashAtBank, Segment.Bosa, EntryDirection.Debit, monthly, Narrative: "Check-off remittance"),
                    new(Coa.BosaMemberDepositsControl, Segment.Bosa, EntryDirection.Credit, monthly, SavingsAccount(m.MemberNumber), "Monthly contribution"),
                ]), ct);
                monthCursor = monthCursor.AddMonths(1);
                n++;
            }

            // FOSA: cash deposit at teller
            var depositDate = joinDate.AddDays(rng.Next(10, 90));
            if (depositDate <= today)
            {
                posted += await PostIfMissing(existingRefs, new PostingRequest($"FOSA-DEP:{m.MemberNumber}:1", $"Cash deposit — {m.FirstName} {m.LastName}", depositDate, "Seed", teller,
                [
                    new(Coa.FosaCashOnHand, Segment.Fosa, EntryDirection.Debit, fosaDeposit, Narrative: "Cash at counter"),
                    new(Coa.FosaSavingsControl, Segment.Fosa, EntryDirection.Credit, fosaDeposit, FosaAccount(m.MemberNumber), "Cash deposit"),
                ]), ct);

                // withdrawal + ledger fee for about half the members
                if (i % 2 == 0)
                {
                    var wd = Math.Floor(fosaDeposit * 0.4m / 100m) * 100m;
                    posted += await PostIfMissing(existingRefs, new PostingRequest($"FOSA-WD:{m.MemberNumber}:1", $"Cash withdrawal — {m.FirstName} {m.LastName}", depositDate.AddDays(rng.Next(5, 40)), "Seed", teller,
                    [
                        new(Coa.FosaSavingsControl, Segment.Fosa, EntryDirection.Debit, wd + 50m, FosaAccount(m.MemberNumber), "Cash withdrawal incl. KES 50 fee"),
                        new(Coa.FosaCashOnHand, Segment.Fosa, EntryDirection.Credit, wd, Narrative: "Cash paid"),
                        new(Coa.FosaFeesAndCharges, Segment.Fosa, EntryDirection.Credit, 50m, Narrative: "Withdrawal fee"),
                    ]), ct);
                }
            }
        }

        // 3. Teller banks excess cash; bank charges; SDGF accrual.
        posted += await PostIfMissing(existingRefs, new PostingRequest("BANKING:2026-06-30", "Teller cash banked", today.AddMonths(-2), "Seed", teller,
        [
            new(Coa.FosaCashAtBank, Segment.Fosa, EntryDirection.Debit, 150_000m, Narrative: "Cash deposit to bank"),
            new(Coa.FosaCashOnHand, Segment.Fosa, EntryDirection.Credit, 150_000m, Narrative: "Cash banked"),
        ]), ct);
        posted += await PostIfMissing(existingRefs, new PostingRequest("BANKCHG:2026-07", "Bank charges — July", today.AddMonths(-2).AddDays(3), "Seed", system,
        [
            new(Coa.FosaBankCharges, Segment.Fosa, EntryDirection.Debit, 3_450m, Narrative: "Monthly ledger fees"),
            new(Coa.FosaCashAtBank, Segment.Fosa, EntryDirection.Credit, 3_450m, Narrative: "Monthly ledger fees"),
        ]), ct);
        posted += await PostIfMissing(existingRefs, new PostingRequest("SDGF:2026-Q2", "SDGF contribution accrual — Q2", today.AddMonths(-3), "Seed", system,
        [
            new(Coa.BosaSdgfExpense, Segment.Bosa, EntryDirection.Debit, 12_500m, Narrative: "Deposit guarantee fund levy"),
            new(Coa.BosaSdgfPayable, Segment.Bosa, EntryDirection.Credit, 12_500m, Narrative: "Deposit guarantee fund levy"),
        ]), ct);

        logger.Created("Journal entries", posted);

        // 4. A manual journal left pending approval (maker: accountant) for the maker-checker demo.
        const string pendingRef = "MJ-2026-0007";
        if (!existingRefs.Contains(pendingRef))
        {
            var glBank = await db.GlAccounts.AsNoTracking().FirstAsync(a => a.Code == Coa.FosaCashAtBank, ct);
            var glSuspense = await db.GlAccounts.AsNoTracking().FirstAsync(a => a.Code == Coa.FosaSuspense, ct);
            var pending = JournalEntry.Create(Ids.New(), DemoTenant.Id, pendingRef, "Unidentified bank credit parked in suspense pending member identification", today.AddDays(-1), "Manual", DemoTenant.Users.Accountant, clock.UtcNow,
            [
                new JournalLineDraft(glBank.Id, glBank.Code, null, null, Segment.Fosa, EntryDirection.Debit, 7_800m, "Bank statement line 14"),
                new JournalLineDraft(glSuspense.Id, glSuspense.Code, null, null, Segment.Fosa, EntryDirection.Credit, 7_800m, "Awaiting identification"),
            ], JournalEntryStatus.PendingApproval);
            db.JournalEntries.Add(pending);
            await db.SaveChangesAsync(ct);
            logger.Created("Pending manual journal", 1);
        }
    }

    private async Task<int> OpenIfMissing(HashSet<string> existing, string number, Guid memberId, string control, Segment segment, LedgerAccountKind kind, string product, CancellationToken ct)
    {
        if (existing.Contains(number)) return 0;
        // Goes through the Savings module so the product-level account row and the ledger sub-account are created together.
        var opened = await savings.OpenAccountAsync(memberId, product, DemoTenant.Users.System, ct);
        if (opened.AccountNumber != number) throw new InvalidOperationException($"Expected account number {number} but the product produced {opened.AccountNumber}.");
        existing.Add(number);
        return 1;
    }

    private async Task<int> PostIfMissing(HashSet<string> existing, PostingRequest request, CancellationToken ct)
    {
        if (existing.Contains(request.Reference)) return 0;
        await ledger.PostAsync(request, ct);
        existing.Add(request.Reference);
        return 1;
    }
}
