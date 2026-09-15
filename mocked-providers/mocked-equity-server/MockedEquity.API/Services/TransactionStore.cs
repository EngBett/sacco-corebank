using System.Collections.Concurrent;
using MockedEquity.API.Models;

namespace MockedEquity.API.Services;

/// <summary>
/// In-memory transaction store. Indexed by both the request id we were sent and the transaction id
/// the mock generated, because the status endpoint is documented to accept either.
/// </summary>
public interface ITransactionStore
{
    /// <summary>Stores a new transaction. Returns false if the request id was already used.</summary>
    bool TryAdd(EquityTransaction transaction);

    /// <summary>Looks a transaction up by request id or by generated transaction id.</summary>
    EquityTransaction? Find(string? identifier);

    void Update(EquityTransaction transaction);

    IReadOnlyCollection<EquityTransaction> All();

    /// <summary>Forgets everything — test support only.</summary>
    void Clear();
}

public class TransactionStore : ITransactionStore
{
    private readonly ConcurrentDictionary<string, EquityTransaction> _byRequestId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, EquityTransaction> _byTransactionId = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAdd(EquityTransaction transaction)
    {
        // Jenga rejects a reused requestId; reproducing that keeps our idempotency handling honest.
        if (!_byRequestId.TryAdd(transaction.RequestId, transaction))
            return false;

        _byTransactionId[transaction.TransactionId] = transaction;
        return true;
    }

    public EquityTransaction? Find(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

        return _byRequestId.TryGetValue(identifier, out var byRequest)
            ? byRequest
            : _byTransactionId.GetValueOrDefault(identifier);
    }

    public void Update(EquityTransaction transaction)
    {
        transaction.UpdatedAt = DateTime.UtcNow;
        _byRequestId[transaction.RequestId] = transaction;
        _byTransactionId[transaction.TransactionId] = transaction;
    }

    public IReadOnlyCollection<EquityTransaction> All() => _byRequestId.Values.ToArray();

    public void Clear()
    {
        _byRequestId.Clear();
        _byTransactionId.Clear();
    }
}
