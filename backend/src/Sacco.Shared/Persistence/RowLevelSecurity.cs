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

    public static void DisableTenantIsolation(this MigrationBuilder mb, string schema, string table)
    {
        mb.Sql($"""
            DROP POLICY IF EXISTS tenant_isolation ON "{schema}"."{table}";
            ALTER TABLE "{schema}"."{table}" DISABLE ROW LEVEL SECURITY;
            """);
    }
}
