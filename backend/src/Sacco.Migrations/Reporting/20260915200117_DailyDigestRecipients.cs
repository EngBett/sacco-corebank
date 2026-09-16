using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Sacco.Shared.Persistence;

#nullable disable

namespace Sacco.Migrations.Reporting
{
    /// <inheritdoc />
    public partial class DailyDigestRecipients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "report_recipients",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    added_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_recipients", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_report_recipients_tenant_id",
                schema: "reporting",
                table: "report_recipients",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_recipients_tenant_id_email",
                schema: "reporting",
                table: "report_recipients",
                columns: new[] { "tenant_id", "email" },
                unique: true);

            migrationBuilder.EnableTenantIsolation("reporting", "report_recipients");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "report_recipients",
                schema: "reporting");
        }
    }
}
