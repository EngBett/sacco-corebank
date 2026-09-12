using Sacco.Shared.Domain;
using Sacco.Shared.Ledger;
using Sacco.Shared.Savings;

namespace Sacco.Modules.Savings.Application;

/// <summary>
/// GL mapping the Savings module posts against. Defaults match the demo chart of accounts;
/// a real SACCO overrides them in configuration (per-tenant configuration is a later phase).
/// </summary>
public sealed class SavingsSettings
{
    public const string SectionName = "Savings";
    public string TellerCashGl { get; set; } = "1010";            // FOSA
    public string FosaBankGl { get; set; } = "1020";              // FOSA
    public string MpesaSettlementGl { get; set; } = "1030";       // FOSA
    public string AirtelSettlementGl { get; set; } = "1040";      // FOSA
    public string BosaBankGl { get; set; } = "1000";              // BOSA (check-off remittances)
    public string DueFromFosaGl { get; set; } = "1800";           // BOSA clearing
    public string DueToBosaGl { get; set; } = "2800";             // FOSA clearing
    public string RetainedEarningsGl { get; set; } = "3200";      // BOSA
    public string DividendsPayableGl { get; set; } = "2400";      // BOSA
    public string InterestPayableGl { get; set; } = "2300";       // BOSA
    public string WithholdingTaxPayableGl { get; set; } = "2700"; // BOSA
    public int DividendWithholdingTaxBps { get; set; } = 500;     // 5% WHT on dividends/interest (KRA)

    public (string Gl, Segment Segment) SettlementFor(DepositChannel channel) => channel switch
    {
        DepositChannel.Cash => (TellerCashGl, Segment.Fosa),
        DepositChannel.MPesa => (MpesaSettlementGl, Segment.Fosa),
        DepositChannel.AirtelMoney => (AirtelSettlementGl, Segment.Fosa),
        DepositChannel.BankTransfer => (FosaBankGl, Segment.Fosa),
        DepositChannel.CheckOff => (BosaBankGl, Segment.Bosa),
        _ => throw new DomainRuleException("savings.channel_unsupported", $"Channel {channel} has no settlement account."),
    };
}

/// <summary>
/// Builds balanced, per-segment-balanced postings. When money moves between a FOSA settlement
/// account and a BOSA member account (or vice versa) the inter-segment clearing pair is inserted so
/// each segment balances on its own (ADR 0002; ledger invariant).
/// </summary>
public static class PostingBuilder
{
    /// <summary>Money comes IN via <paramref name="settlementGl"/> and lands on the member account.</summary>
    public static List<PostingLine> Inflow(SavingsSettings s, string settlementGl, Segment settlementSegment, string controlGl, string accountNumber, Segment accountSegment, decimal amount, string narrative)
    {
        var lines = new List<PostingLine> { new(settlementGl, settlementSegment, EntryDirection.Debit, amount, Narrative: narrative) };
        if (settlementSegment != accountSegment)
            lines.AddRange(Bridge(s, settlementSegment, accountSegment, amount, narrative));
        lines.Add(new PostingLine(controlGl, accountSegment, EntryDirection.Credit, amount, accountNumber, narrative));
        return lines;
    }

    /// <summary>Money goes OUT from the member account via <paramref name="settlementGl"/>, optionally with a fee credited to <paramref name="feeGl"/> (same segment as the account).</summary>
    public static List<PostingLine> Outflow(SavingsSettings s, string settlementGl, Segment settlementSegment, string controlGl, string accountNumber, Segment accountSegment, decimal amount, decimal fee, string? feeGl, string narrative)
    {
        var lines = new List<PostingLine> { new(controlGl, accountSegment, EntryDirection.Debit, amount + fee, accountNumber, narrative) };
        if (fee > 0) lines.Add(new PostingLine(feeGl!, accountSegment, EntryDirection.Credit, fee, Narrative: "Withdrawal fee"));
        if (settlementSegment != accountSegment)
            lines.AddRange(Bridge(s, accountSegment, settlementSegment, amount, narrative));
        lines.Add(new PostingLine(settlementGl, settlementSegment, EntryDirection.Credit, amount, Narrative: narrative));
        return lines;
    }

    /// <summary>Clearing lines moving <paramref name="amount"/> of value from one segment to the other.</summary>
    public static IEnumerable<PostingLine> Bridge(SavingsSettings s, Segment from, Segment to, decimal amount, string narrative)
        => SegmentBridge.Lines(s.DueFromFosaGl, s.DueToBosaGl, from, to, amount, narrative);
}
