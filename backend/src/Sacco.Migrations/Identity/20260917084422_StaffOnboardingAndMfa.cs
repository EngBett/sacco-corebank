using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Sacco.Shared.Persistence;

#nullable disable

namespace Sacco.Migrations.Identity
{
    /// <inheritdoc />
    public partial class StaffOnboardingAndMfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "activated_at",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "totp_enabled_at",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "totp_last_used_step",
                schema: "identity",
                table: "users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "totp_pending_secret",
                schema: "identity",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "totp_secret",
                schema: "identity",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "staff_account_tokens",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_account_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "staff_invitations",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    role_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    proposed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_invitations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "staff_recovery_codes",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_recovery_codes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_staff_account_tokens_tenant_id",
                schema: "identity",
                table: "staff_account_tokens",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_account_tokens_tenant_id_user_id_purpose",
                schema: "identity",
                table: "staff_account_tokens",
                columns: new[] { "tenant_id", "user_id", "purpose" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_account_tokens_token_hash",
                schema: "identity",
                table: "staff_account_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_invitations_tenant_id",
                schema: "identity",
                table: "staff_invitations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_invitations_tenant_id_status",
                schema: "identity",
                table: "staff_invitations",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_recovery_codes_tenant_id",
                schema: "identity",
                table: "staff_recovery_codes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_recovery_codes_tenant_id_user_id",
                schema: "identity",
                table: "staff_recovery_codes",
                columns: new[] { "tenant_id", "user_id" });

            // Everyone who could sign in before invitations existed already chose a password: they count as activated.
            // Tenant isolation is FORCEd on existing databases (it applies to the table owner too), which would make this
            // cross-tenant backfill silently update nothing — lift it for the one statement and restore it straight after.
            migrationBuilder.Sql("""
                ALTER TABLE identity.users NO FORCE ROW LEVEL SECURITY;
                UPDATE identity.users SET activated_at = created_at WHERE activated_at IS NULL AND password_hash <> '';
                ALTER TABLE identity.users FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.EnableTenantIsolation("identity", "staff_invitations");
            migrationBuilder.EnableTenantIsolation("identity", "staff_account_tokens");
            migrationBuilder.EnableTenantIsolation("identity", "staff_recovery_codes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "staff_account_tokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "staff_invitations",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "staff_recovery_codes",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "activated_at",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "totp_enabled_at",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "totp_last_used_step",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "totp_pending_secret",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "totp_secret",
                schema: "identity",
                table: "users");
        }
    }
}
