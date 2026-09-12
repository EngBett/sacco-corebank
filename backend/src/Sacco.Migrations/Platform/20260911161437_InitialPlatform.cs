using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Sacco.Shared.Persistence;

#nullable disable

namespace Sacco.Migrations.Platform
{
    /// <inheritdoc />
    public partial class InitialPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    short_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sasra_licence_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    custom_domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    branding_primary_color = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    branding_secondary_color = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    branding_accent_color = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    branding_logo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    branding_tagline = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    branding_support_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    branding_support_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_tenant_id",
                schema: "platform",
                table: "audit_log",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_tenant_id_entity_type_entity_id",
                schema: "platform",
                table: "audit_log",
                columns: new[] { "tenant_id", "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_tenant_id_occurred_at",
                schema: "platform",
                table: "audit_log",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tenants_custom_domain",
                schema: "platform",
                table: "tenants",
                column: "custom_domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                schema: "platform",
                table: "tenants",
                column: "slug",
                unique: true);

            // Tenant row-level security (defence in depth behind the EF query filters).
            migrationBuilder.EnableTenantIsolation("platform", "audit_log");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantIsolation("platform", "audit_log");

            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "platform");
        }
    }
}
