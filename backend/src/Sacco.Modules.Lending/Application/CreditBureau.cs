using Sacco.Modules.Lending.Domain;
using Sacco.Shared.Time;

namespace Sacco.Modules.Lending.Application;

public sealed record BureauQuery(string NationalIdNumber, string FullName, string PhoneNumber);
public sealed record BureauReport(BureauStatus Status, string? Reference, decimal? ListedAmount, string? Narrative, DateTimeOffset CheckedAt);

/// <summary>
/// Credit reference bureau lookup. Same pattern as payment providers: one contract, a sandbox
/// implementation that needs no credentials, and a live implementation selected by configuration
/// (<c>Lending:CreditBureau:Mode</c>). A failed or unconfigured lookup is reported as
/// <see cref="BureauStatus.Unavailable"/> so scoring degrades rather than blocks origination.
/// </summary>
public interface ICreditBureau
{
    string Name { get; }
    Task<BureauReport> CheckAsync(BureauQuery query, CancellationToken ct);
}

/// <summary>
/// Deterministic sandbox: the last digit of the national ID decides the outcome so demos are
/// repeatable. `0` → listed, `9` → bureau unavailable, anything else → clear.
/// </summary>
public sealed class SandboxCreditBureau(IClock clock) : ICreditBureau
{
    public string Name => "Sandbox CRB";

    public Task<BureauReport> CheckAsync(BureauQuery query, CancellationToken ct)
    {
        var last = query.NationalIdNumber.Trim().LastOrDefault();
        var reference = $"SBX-{query.NationalIdNumber.Trim()[^Math.Min(6, query.NationalIdNumber.Trim().Length)..]}";
        BureauReport report = last switch
        {
            '0' => new(BureauStatus.Listed, reference, 45_000m, "Sandbox listing: KES 45,000.00 with another lender, 120+ days past due (non-performing).", clock.UtcNow),
            '9' => new(BureauStatus.Unavailable, null, null, "Sandbox: bureau timed out; scored without a bureau result.", clock.UtcNow),
            _ => new(BureauStatus.Clear, reference, null, "Sandbox: no adverse listings.", clock.UtcNow),
        };
        return Task.FromResult(report);
    }
}

/// <summary>Placeholder until a bureau integration (TransUnion, Metropol, Creditinfo) is written; every lookup is reported as unavailable, never as clear.</summary>
public sealed class LiveCreditBureauNotConfigured(IClock clock) : ICreditBureau
{
    public string Name => "Credit bureau (not configured)";
    public Task<BureauReport> CheckAsync(BureauQuery query, CancellationToken ct)
        => Task.FromResult(new BureauReport(BureauStatus.Unavailable, null, null, "Lending:CreditBureau:Mode is Live but no bureau provider is configured.", clock.UtcNow));
}
