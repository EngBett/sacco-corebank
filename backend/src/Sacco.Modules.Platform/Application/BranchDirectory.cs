using Microsoft.EntityFrameworkCore;
using Sacco.Modules.Platform.Domain;
using Sacco.Modules.Platform.Persistence;
using Sacco.Shared.Audit;
using Sacco.Shared.Domain;
using Sacco.Shared.Tenancy;

namespace Sacco.Modules.Platform.Application;

/// <summary>Branch registry and the <see cref="IBranchDirectory"/> other modules use (ADR 0018).</summary>
public sealed class BranchService(PlatformDbContext db, ITenantContext tenant, IAuditLogger audit) : IBranchDirectory
{
    public async Task<IReadOnlyList<BranchInfo>> ListAsync(bool activeOnly, CancellationToken ct) =>
        await db.Branches.AsNoTracking().Where(b => !activeOnly || b.IsActive)
            .OrderByDescending(b => b.IsHeadOffice).ThenBy(b => b.DisplayOrder).ThenBy(b => b.Name)
            .Select(b => new BranchInfo(b.Id, b.Code, b.Name, b.IsHeadOffice, b.IsActive)).ToListAsync(ct);

    public async Task<BranchInfo?> FindAsync(Guid branchId, CancellationToken ct) =>
        await db.Branches.AsNoTracking().Where(b => b.Id == branchId)
            .Select(b => new BranchInfo(b.Id, b.Code, b.Name, b.IsHeadOffice, b.IsActive)).FirstOrDefaultAsync(ct);

    public async Task<BranchInfo?> FindByCodeAsync(string code, CancellationToken ct)
    {
        var normalised = code.Trim().ToUpperInvariant();
        return await db.Branches.AsNoTracking().Where(b => b.Code == normalised)
            .Select(b => new BranchInfo(b.Id, b.Code, b.Name, b.IsHeadOffice, b.IsActive)).FirstOrDefaultAsync(ct);
    }

    public async Task<BranchInfo?> DefaultAsync(CancellationToken ct) =>
        await db.Branches.AsNoTracking().Where(b => b.IsActive)
            .OrderByDescending(b => b.IsHeadOffice).ThenBy(b => b.DisplayOrder).ThenBy(b => b.Name)
            .Select(b => new BranchInfo(b.Id, b.Code, b.Name, b.IsHeadOffice, b.IsActive)).FirstOrDefaultAsync(ct);

    public async Task EnsureExistsAsync(Guid branchId, CancellationToken ct)
    {
        if (!await db.Branches.AnyAsync(b => b.Id == branchId, ct))
            throw new DomainRuleException("platform.branch.unknown", "That branch does not belong to this SACCO.");
    }

    public Task<List<Branch>> ListForAdminAsync(CancellationToken ct) =>
        db.Branches.AsNoTracking().OrderByDescending(b => b.IsHeadOffice).ThenBy(b => b.DisplayOrder).ThenBy(b => b.Name).ToListAsync(ct);

    public async Task<Branch> CreateAsync(string code, string name, bool isHeadOffice, string? county, string? town, string? address,
        string? phone, string? email, int displayOrder, Guid byUser, CancellationToken ct)
    {
        var branch = Branch.Create(Ids.New(), tenant.TenantId, code, name, isHeadOffice: false, DateTimeOffset.UtcNow);
        branch.Update(code, name, isHeadOffice, county, town, address, phone, email, displayOrder, isActive: true);
        if (isHeadOffice) await DemoteOtherHeadOfficesAsync(branch.Id, ct);
        db.Branches.Add(branch);
        await SaveAsync(ct, code);
        await audit.RecordAsync(new AuditEvent("platform.branch.created", nameof(Branch), branch.Id.ToString(), byUser, $$"""{"code":"{{branch.Code}}","name":"{{branch.Name}}"}"""), ct);
        return branch;
    }

    public async Task<Branch> UpdateAsync(Guid id, string code, string name, bool isHeadOffice, string? county, string? town, string? address,
        string? phone, string? email, int displayOrder, bool isActive, Guid byUser, CancellationToken ct)
    {
        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Branch", id);
        if (isHeadOffice) await DemoteOtherHeadOfficesAsync(branch.Id, ct);
        else if (branch.IsHeadOffice)
            throw new DomainRuleException("platform.branch.head_office_required", "Make another branch the head office before removing it from this one.");
        branch.Update(code, name, isHeadOffice, county, town, address, phone, email, displayOrder, isActive);
        await SaveAsync(ct, code);
        await audit.RecordAsync(new AuditEvent("platform.branch.updated", nameof(Branch), branch.Id.ToString(), byUser, $$"""{"code":"{{branch.Code}}","active":{{(branch.IsActive ? "true" : "false")}}}"""), ct);
        return branch;
    }

    private async Task DemoteOtherHeadOfficesAsync(Guid keepId, CancellationToken ct)
    {
        foreach (var other in await db.Branches.Where(b => b.IsHeadOffice && b.Id != keepId).ToListAsync(ct))
            other.SetHeadOffice(false);
    }

    private async Task SaveAsync(CancellationToken ct, string code)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { throw new ConflictException("platform.branch.duplicate", $"A branch with code '{code.Trim().ToUpperInvariant()}' already exists."); }
    }
}
