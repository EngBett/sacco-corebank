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

    private void Apply(DbConnection connection)
    {
        if (!tenant.HasTenant) return;
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SET {SettingName} = '{tenant.TenantId:D}'";
        cmd.ExecuteNonQuery();
    }

    private async Task ApplyAsync(DbConnection connection, CancellationToken ct)
    {
        if (!tenant.HasTenant) return;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SET {SettingName} = '{tenant.TenantId:D}'";
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
