using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Sacco.Shared.Persistence;

#nullable disable

namespace Sacco.Migrations.Reporting
{
    /// <inheritdoc />
    public partial class InitialReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reporting");

            migrationBuilder.CreateTable(
                name: "statutory_returns",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    generated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submission_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_reconciled = table.Column<bool>(type: "boolean", nullable: false),
                    reconciliation_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    package = table.Column<string>(type: "jsonb", nullable: false),
                    withdrawal_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statutory_returns", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_statutory_returns_tenant_id",
                schema: "reporting",
                table: "statutory_returns",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_statutory_returns_tenant_id_period_end",
                schema: "reporting",
                table: "statutory_returns",
                columns: new[] { "tenant_id", "period_end" });

            // Tenant row-level security (defence in depth behind the EF query filters).
            migrationBuilder.EnableTenantIsolation("reporting", "statutory_returns");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantIsolation("reporting", "statutory_returns");

            migrationBuilder.DropTable(
                name: "statutory_returns",
                schema: "reporting");
        }
    }
}
