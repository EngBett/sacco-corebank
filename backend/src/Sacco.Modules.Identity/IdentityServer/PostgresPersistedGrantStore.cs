using Microsoft.EntityFrameworkCore;
using Open.IdentityServer.Models;
using Open.IdentityServer.Stores;
using Sacco.Modules.Identity.Domain;
using Sacco.Modules.Identity.Persistence;

namespace Sacco.Modules.Identity.IdentityServer;

public sealed class PostgresPersistedGrantStore(IdentityDbContext db) : IPersistedGrantStore
{
    public async Task StoreAsync(PersistedGrant grant)
    {
        var existing = await db.PersistedGrants.FindAsync(grant.Key);
        if (existing is null)
        {
            db.PersistedGrants.Add(new PersistedGrantRecord
            {
                Key = grant.Key, Type = grant.Type, SubjectId = grant.SubjectId, SessionId = grant.SessionId, ClientId = grant.ClientId,
                Description = grant.Description, CreationTime = grant.CreationTime, Expiration = grant.Expiration, ConsumedTime = grant.ConsumedTime, Data = grant.Data,
            });
        }
        else
        {
            existing.Type = grant.Type; existing.SubjectId = grant.SubjectId; existing.SessionId = grant.SessionId; existing.ClientId = grant.ClientId;
            existing.Description = grant.Description; existing.CreationTime = grant.CreationTime; existing.Expiration = grant.Expiration; existing.ConsumedTime = grant.ConsumedTime; existing.Data = grant.Data;
        }
        await db.SaveChangesAsync();
    }

    public async Task<PersistedGrant?> GetAsync(string key)
    {
        var r = await db.PersistedGrants.AsNoTracking().FirstOrDefaultAsync(g => g.Key == key);
        return r is null ? null : Map(r);
    }

    public async Task<IEnumerable<PersistedGrant>> GetAllAsync(PersistedGrantFilter filter)
        => (await Filter(filter).AsNoTracking().ToListAsync()).Select(Map);

    public async Task RemoveAsync(string key)
        => await db.PersistedGrants.Where(g => g.Key == key).ExecuteDeleteAsync();

    public async Task RemoveAllAsync(PersistedGrantFilter filter)
        => await Filter(filter).ExecuteDeleteAsync();

    private IQueryable<PersistedGrantRecord> Filter(PersistedGrantFilter f)
    {
        var q = db.PersistedGrants.AsQueryable();
        if (!string.IsNullOrEmpty(f.SubjectId)) q = q.Where(g => g.SubjectId == f.SubjectId);
        if (!string.IsNullOrEmpty(f.SessionId)) q = q.Where(g => g.SessionId == f.SessionId);
        if (!string.IsNullOrEmpty(f.ClientId)) q = q.Where(g => g.ClientId == f.ClientId);
        if (!string.IsNullOrEmpty(f.Type)) q = q.Where(g => g.Type == f.Type);
        return q;
    }

    private static PersistedGrant Map(PersistedGrantRecord r) => new()
    {
        Key = r.Key, Type = r.Type, SubjectId = r.SubjectId, SessionId = r.SessionId, ClientId = r.ClientId,
        Description = r.Description, CreationTime = r.CreationTime, Expiration = r.Expiration, ConsumedTime = r.ConsumedTime, Data = r.Data,
    };
}
