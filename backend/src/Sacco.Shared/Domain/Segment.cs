namespace Sacco.Shared.Domain;

/// <summary>
/// The FOSA/BOSA GL dimension (ADR 0002). Every account and every journal line carries one
/// of these explicitly. Reporting slices one ledger by this tag; it is never two ledgers.
/// </summary>
public enum Segment
{
    /// <summary>Front Office Services Activity — on-demand, teller-style banking.</summary>
    Fosa = 1,

    /// <summary>Back Office Services Activity — traditional notice-period deposits, shares, loans.</summary>
    Bosa = 2,
}

public enum EntryDirection
{
    Debit = 1,
    Credit = 2,
}
