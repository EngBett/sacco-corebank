using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Sacco.Shared.Persistence;

#nullable disable

namespace Sacco.Migrations.Platform
{
    /// <inheritdoc />
    public partial class BranchesAndAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Details is hashed as the exact string the application wrote; jsonb would hand a re-formatted string back on
            // read and break every hash. Plain text also lets the audit search ILIKE over it.
            migrationBuilder.Sql("ALTER TABLE platform.audit_log ALTER COLUMN details TYPE text USING details::text;");

            migrationBuilder.AddColumn<string>(
                name: "actor_name",
                schema: "platform",
                table: "audit_log",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "platform",
                table: "audit_log",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hash",
                schema: "platform",
                table: "audit_log",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ip_address",
                schema: "platform",
                table: "audit_log",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            // Entries written before outcomes existed all recorded something that happened, so they are successes — an
            // empty string is not a value this column's enum can ever mean, and would hide those rows from the filter.
            migrationBuilder.AddColumn<string>(
                name: "outcome",
                schema: "platform",
                table: "audit_log",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Success");

            migrationBuilder.AddColumn<string>(
                name: "previous_hash",
                schema: "platform",
                table: "audit_log",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_agent",
                schema: "platform",
                table: "audit_log",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "branches",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    is_head_office = table.Column<bool>(type: "boolean", nullable: false),
                    county = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    town = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    physical_address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branches", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_tenant_id_action",
                schema: "platform",
                table: "audit_log",
                columns: new[] { "tenant_id", "action" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_tenant_id_actor_user_id",
                schema: "platform",
                table: "audit_log",
                columns: new[] { "tenant_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_tenant_id_branch_id",
                schema: "platform",
                table: "audit_log",
                columns: new[] { "tenant_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_branches_tenant_id",
                schema: "platform",
                table: "branches",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_branches_tenant_id_code",
                schema: "platform",
                table: "branches",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.EnableTenantIsolation("platform", "branches");

            // The audit trail is append-only (ADR 0019): the database itself refuses edits and deletes, so a compromised
            // application account can add entries but never quietly rewrite or remove them. Entries written before the
            // hash chain existed keep an empty hash and are reported as unchained rather than as tampered.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION platform.audit_log_is_append_only() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'platform.audit_log is append-only: % is not allowed', TG_OP
                        USING ERRCODE = 'raise_exception';
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS audit_log_append_only ON platform.audit_log;
                CREATE TRIGGER audit_log_append_only
                    BEFORE UPDATE OR DELETE ON platform.audit_log
                    FOR EACH ROW EXECUTE FUNCTION platform.audit_log_is_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS audit_log_append_only ON platform.audit_log;
                DROP FUNCTION IF EXISTS platform.audit_log_is_append_only();
                """);

            migrationBuilder.DropTable(
                name: "branches",
                schema: "platform");

            migrationBuilder.DropIndex(
                name: "ix_audit_log_tenant_id_action",
                schema: "platform",
                table: "audit_log");

            migrationBuilder.DropIndex(
                name: "ix_audit_log_tenant_id_actor_user_id",
                schema: "platform",
                table: "audit_log");

            migrationBuilder.DropIndex(
                name: "ix_audit_log_tenant_id_branch_id",
                schema: "platform",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "actor_name",
                schema: "platform",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "platform",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "hash",
                schema: "platform",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "ip_address",
                schema: "platform",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "outcome",
                schema: "platform",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "previous_hash",
                schema: "platform",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "user_agent",
                schema: "platform",
                table: "audit_log");

            migrationBuilder.Sql("ALTER TABLE platform.audit_log ALTER COLUMN details TYPE jsonb USING details::jsonb;");
        }
    }
}
