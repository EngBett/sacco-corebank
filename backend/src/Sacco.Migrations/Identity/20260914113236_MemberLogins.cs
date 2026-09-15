using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Sacco.Shared.Persistence;

#nullable disable

namespace Sacco.Migrations.Identity
{
    /// <inheritdoc />
    public partial class MemberLogins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "member_logins",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    display_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    pin_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_out_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_logins", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_member_logins_tenant_id",
                schema: "identity",
                table: "member_logins",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_logins_tenant_id_member_id",
                schema: "identity",
                table: "member_logins",
                columns: new[] { "tenant_id", "member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_member_logins_tenant_id_phone_number",
                schema: "identity",
                table: "member_logins",
                columns: new[] { "tenant_id", "phone_number" });
            migrationBuilder.EnableTenantIsolation("identity", "member_logins");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "member_logins",
                schema: "identity");
        }
    }
}
