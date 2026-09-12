using Sacco.Shared.Domain;

namespace Sacco.Shared.Ledger;

/// <summary>
/// Clearing lines that move value between the FOSA and BOSA segments so that every journal
/// balances within each segment (ADR 0002). <paramref name="from"/> is the segment where the
/// value originates (cash received / asset released), <paramref name="to"/> where it lands.
/// </summary>
public static class SegmentBridge
{
    public static IEnumerable<PostingLine> Lines(string dueFromFosaGl, string dueToBosaGl, Segment from, Segment to, decimal amount, string? narrative)
    {
        if (from == to || amount <= 0) yield break;
        if (from == Segment.Fosa)
        {
            // FOSA holds value that belongs to BOSA: FOSA owes BOSA more, BOSA is owed more.
            yield return new PostingLine(dueToBosaGl, Segment.Fosa, EntryDirection.Credit, amount, Narrative: narrative);
            yield return new PostingLine(dueFromFosaGl, Segment.Bosa, EntryDirection.Debit, amount, Narrative: narrative);
        }
        else
        {
            // BOSA value is delivered through FOSA: BOSA's receivable from FOSA falls, FOSA's payable to BOSA falls.
            yield return new PostingLine(dueFromFosaGl, Segment.Bosa, EntryDirection.Credit, amount, Narrative: narrative);
            yield return new PostingLine(dueToBosaGl, Segment.Fosa, EntryDirection.Debit, amount, Narrative: narrative);
        }
    }
}
