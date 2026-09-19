using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Sacco.Shared.Persistence;

#nullable disable

namespace Sacco.Migrations.Identity
{
    /// <inheritdoc />
    public partial class MemberDeviceTrustAndOtp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "member_otp_challenges",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_otp_challenges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "member_trusted_devices",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    device_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    trusted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_trusted_devices", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_member_otp_challenges_tenant_id",
                schema: "identity",
                table: "member_otp_challenges",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_otp_challenges_tenant_id_member_id_device_id",
                schema: "identity",
                table: "member_otp_challenges",
                columns: new[] { "tenant_id", "member_id", "device_id" });

            migrationBuilder.CreateIndex(
                name: "ix_member_trusted_devices_tenant_id",
                schema: "identity",
                table: "member_trusted_devices",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_trusted_devices_tenant_id_member_id_device_id",
                schema: "identity",
                table: "member_trusted_devices",
                columns: new[] { "tenant_id", "member_id", "device_id" },
                unique: true);

            migrationBuilder.EnableTenantIsolation("identity", "member_otp_challenges");
            migrationBuilder.EnableTenantIsolation("identity", "member_trusted_devices");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "member_otp_challenges",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "member_trusted_devices",
                schema: "identity");
        }
    }
}
