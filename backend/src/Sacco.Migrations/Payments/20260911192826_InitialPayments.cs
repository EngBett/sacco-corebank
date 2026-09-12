using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Sacco.Shared.Persistence;

#nullable disable

namespace Sacco.Migrations.Payments
{
    /// <inheritdoc />
    public partial class InitialPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateTable(
                name: "processed_provider_transactions",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    provider_transaction_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payment_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outcome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_provider_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    counterparty = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purpose_type = table.Column<int>(type: "integer", nullable: false),
                    purpose_account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    purpose_loan_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    purpose_withdrawal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    our_reference = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    provider_request_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider_transaction_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ledger_journal_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    initiated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    initiated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    callback_count = table.Column<int>(type: "integer", nullable: false),
                    narrative = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transactions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_processed_provider_transactions_tenant_id",
                schema: "payments",
                table: "processed_provider_transactions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_processed_provider_transactions_reference",
                schema: "payments",
                table: "processed_provider_transactions",
                columns: new[] { "provider", "provider_transaction_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_tenant_id",
                schema: "payments",
                table: "transactions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_tenant_id_our_reference",
                schema: "payments",
                table: "transactions",
                columns: new[] { "tenant_id", "our_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_tenant_id_provider_provider_request_id",
                schema: "payments",
                table: "transactions",
                columns: new[] { "tenant_id", "provider", "provider_request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_tenant_id_status",
                schema: "payments",
                table: "transactions",
                columns: new[] { "tenant_id", "status" });

            // Tenant row-level security (defence in depth behind the EF query filters).
            migrationBuilder.EnableTenantIsolation("payments", "transactions");
            migrationBuilder.EnableTenantIsolation("payments", "processed_provider_transactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantIsolation("payments", "transactions");
            migrationBuilder.DisableTenantIsolation("payments", "processed_provider_transactions");

            migrationBuilder.DropTable(
                name: "processed_provider_transactions",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "transactions",
                schema: "payments");
        }
    }
}
