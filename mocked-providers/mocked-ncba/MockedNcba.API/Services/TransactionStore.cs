using System.Collections.Concurrent;
using MockedNcba.API.Models;

namespace MockedNcba.API.Services;

/// <summary>
/// In-memory store for mock NCBA transactions.
/// Keyed by request Reference (for duplicate detection on the same transaction reference).
/// </summary>
public interface ITransactionStore
{
    void Add(string reference, NcbaTransactionRecord record);
    NcbaTransactionRecord? GetByReference(string reference);
}

public class TransactionStore : ITransactionStore
{
    private readonly ConcurrentDictionary<string, NcbaTransactionRecord> _transactions = new();

    public void Add(string reference, NcbaTransactionRecord record) =>
        _transactions[reference] = record;

    public NcbaTransactionRecord? GetByReference(string reference) =>
        _transactions.TryGetValue(reference, out var record) ? record : null;
}

public class NcbaTransactionRecord
{
    public string Reference          { get; set; } = string.Empty;
    public string TxnReferenceNo     { get; set; } = string.Empty;
    public string TranType           { get; set; } = string.Empty;
    public string BeneficiaryAccount { get; set; } = string.Empty;
    public decimal Amount            { get; set; }
    public string Currency           { get; set; } = "KES";
    public string Narration          { get; set; } = string.Empty;
    public DateTime CreatedAt        { get; set; } = DateTime.UtcNow;
}
