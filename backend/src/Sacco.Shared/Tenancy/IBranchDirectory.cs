namespace Sacco.Shared.Tenancy;

/// <param name="IsHeadOffice">Exactly one per tenant; used as the fallback branch when nothing else names one.</param>
public sealed record BranchInfo(Guid Id, string Code, string Name, bool IsHeadOffice, bool IsActive);

/// <summary>
/// Read-only branch lookup for modules that carry a branch dimension (members, staff, ledger postings) without
/// depending on the Platform module. Implemented there; see ADR 0018.
/// </summary>
public interface IBranchDirectory
{
    Task<IReadOnlyList<BranchInfo>> ListAsync(bool activeOnly, CancellationToken ct);
    Task<BranchInfo?> FindAsync(Guid branchId, CancellationToken ct);
    Task<BranchInfo?> FindByCodeAsync(string code, CancellationToken ct);

    /// <summary>The head office, or the first active branch when none is marked; null when the tenant has no branches.</summary>
    Task<BranchInfo?> DefaultAsync(CancellationToken ct);

    /// <summary>Throws <c>platform.branch.unknown</c> when the id is not a branch of this tenant.</summary>
    Task EnsureExistsAsync(Guid branchId, CancellationToken ct);
}
