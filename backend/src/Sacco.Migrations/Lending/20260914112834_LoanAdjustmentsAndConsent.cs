using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Sacco.Shared.Persistence;

#nullable disable

namespace Sacco.Migrations.Lending
{
    /// <inheritdoc />
    public partial class LoanAdjustmentsAndConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "bureau_consent_at",
                schema: "lending",
                table: "loans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bureau_consent_text",
                schema: "lending",
                table: "loans",
                type: "character varying(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "restructure_count",
                schema: "lending",
                table: "loans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "loan_adjustments",
                schema: "lending",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    new_term_months = table.Column<int>(type: "integer", nullable: true),
                    new_interest_rate_bps = table.Column<int>(type: "integer", nullable: true),
                    principal_written_off = table.Column<decimal>(type: "numeric", nullable: true),
                    interest_reversed = table.Column<decimal>(type: "numeric", nullable: true),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_adjustments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_loan_adjustments_loan_id_status",
                schema: "lending",
                table: "loan_adjustments",
                columns: new[] { "loan_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_loan_adjustments_tenant_id",
                schema: "lending",
                table: "loan_adjustments",
                column: "tenant_id");
            migrationBuilder.EnableTenantIsolation("lending", "loan_adjustments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loan_adjustments",
                schema: "lending");

            migrationBuilder.DropColumn(
                name: "bureau_consent_at",
                schema: "lending",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "bureau_consent_text",
                schema: "lending",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "restructure_count",
                schema: "lending",
                table: "loans");
        }
    }
}
