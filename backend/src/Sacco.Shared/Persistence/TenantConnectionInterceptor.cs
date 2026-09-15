using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sacco.Shared.Tenancy;

namespace Sacco.Shared.Persistence;

/// <summary>
/// Publishes the ambient tenant to Postgres as the session setting <c>app.tenant_id</c> so the
/// row-level-security policies created by migrations can enforce isolation for non-owner roles.
/// EF query filters are the first line of defence; RLS is defence in depth.
/// </summary>
public sealed class TenantConnectionInterceptor(ITenantContext tenant) : DbConnectionInterceptor
{
    public const string SettingName = "app.tenant_id";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => Apply(connection);

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        => await ApplyAsync(connection, cancellationToken);

    // Pooled connections keep session settings between uses, so an unresolved tenant clears the setting rather
    // than leaving the previous request's tenant in place: with FORCE RLS such a query sees no tenant rows at all.
    private string Statement => tenant.HasTenant ? $"SET {SettingName} = '{tenant.TenantId:D}'" : $"SET {SettingName} = ''";

    private void Apply(DbConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = Statement;
        cmd.ExecuteNonQuery();
    }

    private async Task ApplyAsync(DbConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = Statement;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
