using Microsoft.EntityFrameworkCore.Migrations;

namespace Sacco.Shared.Persistence;

/// <summary>Helpers for migrations to enable tenant row-level security on a table.</summary>
public static class RowLevelSecurity
{
    /// <summary>
    /// Enables RLS with a tenant-isolation policy keyed on <c>app.tenant_id</c>. Not FORCEd, so the
    /// table owner (migrations, seed tool, local dev) bypasses it; the production application role
    /// must be a non-owner so the policy applies. See infrastructure/CLAUDE.md.
    /// </summary>
    public static void EnableTenantIsolation(this MigrationBuilder mb, string schema, string table)
    {
        mb.Sql($"""
            ALTER TABLE "{schema}"."{table}" ENABLE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS tenant_isolation ON "{schema}"."{table}";
            CREATE POLICY tenant_isolation ON "{schema}"."{table}"
                USING (tenant_id = NULLIF(current_setting('{TenantConnectionInterceptor.SettingName}', true), '')::uuid)
                WITH CHECK (tenant_id = NULLIF(current_setting('{TenantConnectionInterceptor.SettingName}', true), '')::uuid);
            """);
    }

    /// <summary>
    /// Makes every tenant-isolation policy apply to the table owner too (FORCE). Migrations create tables as
    /// the owner, so this runs after them; from then on not even the connection the API uses can read across
    /// tenants, and a query issued without a tenant context returns nothing. Idempotent; safe on every start.
    /// </summary>
    public static async Task ForceTenantIsolationAsync(System.Data.Common.DbConnection connection, CancellationToken ct = default)
    {
        var opened = false;
        if (connection.State != System.Data.ConnectionState.Open) { await connection.OpenAsync(ct); opened = true; }
        try
        {
            var tables = new List<(string Schema, string Table)>();
            await using (var query = connection.CreateCommand())
            {
                query.CommandText = "SELECT schemaname, tablename FROM pg_policies WHERE policyname = 'tenant_isolation'";
                await using var reader = await query.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) tables.Add((reader.GetString(0), reader.GetString(1)));
            }
            foreach (var (schema, table) in tables)
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE \"{schema}\".\"{table}\" FORCE ROW LEVEL SECURITY";
                await alter.ExecuteNonQueryAsync(ct);
            }
        }
        finally { if (opened) await connection.CloseAsync(); }
    }

    public static void DisableTenantIsolation(this MigrationBuilder mb, string schema, string table)
    {
        mb.Sql($"""
            DROP POLICY IF EXISTS tenant_isolation ON "{schema}"."{table}";
            ALTER TABLE "{schema}"."{table}" DISABLE ROW LEVEL SECURITY;
            """);
    }
}
