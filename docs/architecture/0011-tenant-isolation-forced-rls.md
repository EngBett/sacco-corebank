# ADR 0011: Tenant isolation stays row-level security, now forced on the owner

## Status
Accepted (2026-09-14). Closes Phase 8 finding F2 and the open question in ADR 0001 about schema-per-tenant.

## Context
Every tenant-scoped table carries `tenant_id`, an EF query filter, and a Postgres row-level-security policy
keyed on the `app.tenant_id` session setting (ADR 0004, `EnableTenantIsolation`). Until now the policies were
enabled but not *forced*, so the table owner — the very role the API connects as — bypassed them. Isolation
therefore rested on the EF filter alone. Schema-per-tenant was floated as an alternative for SACCOs that demand
stronger separation.

## Decision
1. **Force the policies.** After migrations, both the API host and the seed tool run
   `RowLevelSecurity.ForceTenantIsolationAsync`, which issues `ALTER TABLE … FORCE ROW LEVEL SECURITY` for every
   table carrying the `tenant_isolation` policy. From then on the owner is bound by the policy too; only a Postgres
   superuser bypasses it, and the RDS master user is not one.
2. **Fail closed on the connection.** `TenantConnectionInterceptor` now sets `app.tenant_id` on every connection
   open — to the tenant when one is resolved, to the empty string when none is. Pooled connections can no longer
   carry a previous request's tenant, and a query issued without a tenant context sees no tenant rows at all. The
   integration suite asserts exactly that.
3. **No schema-per-tenant.** One schema per module, one database, RLS forced. A SACCO contract that requires
   physical separation is served by a separate database (a second environment of the same stack), which the
   infrastructure already parameterises, rather than by a second isolation model inside one database.

## Consequences
- Background work must bind a tenant per unit of work (the lending scheduler does) or it reads nothing.
- Tables without `tenant_id` (tenants, persisted grants) are unaffected.
- Migrations that need to touch tenant rows must set `app.tenant_id` explicitly or run as a superuser; none do today.
