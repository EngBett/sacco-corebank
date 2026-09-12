using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Sacco.Shared.Persistence;

#nullable disable

namespace Sacco.Migrations.Savings
{
    /// <inheritdoc />
    public partial class InitialSavings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "savings");

            migrationBuilder.CreateTable(
                name: "dividend_declarations",
                schema: "savings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    financial_year = table.Column<int>(type: "integer", nullable: false),
                    share_dividend_rate_bps = table.Column<int>(type: "integer", nullable: false),
                    deposit_interest_rate_bps = table.Column<int>(type: "integer", nullable: false),
                    withholding_tax_bps = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    declared_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    declared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    total_share_dividend = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    total_deposit_interest = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    total_withholding_tax = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dividend_declarations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "savings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    segment = table.Column<int>(type: "integer", nullable: false),
                    control_gl_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    account_suffix = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    minimum_opening_deposit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    minimum_balance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    allows_withdrawals = table.Column<bool>(type: "boolean", nullable: false),
                    withdrawal_notice_days = table.Column<int>(type: "integer", nullable: false),
                    teller_withdrawal_limit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    withdrawal_fee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    fee_income_gl_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    interest_rate_bps = table.Column<int>(type: "integer", nullable: false),
                    interest_expense_gl_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    term_months = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "withdrawal_requests",
                schema: "savings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    fee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    payout_destination = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notice_expires_on = table.Column<DateOnly>(type: "date", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    journal_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    narrative = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_withdrawal_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dividend_lines",
                schema: "savings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    declaration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shares_account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    share_balance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    share_dividend = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    deposits_account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    deposit_balance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    deposit_interest = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    withholding_tax = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    payout_account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false),
                    journal_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dividend_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_dividend_lines_dividend_declarations_declaration_id",
                        column: x => x.declaration_id,
                        principalSchema: "savings",
                        principalTable: "dividend_declarations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "savings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    segment = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    opened_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    principal = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    term_months = table.Column<int>(type: "integer", nullable: true),
                    interest_rate_bps = table.Column<int>(type: "integer", nullable: true),
                    maturity_date = table.Column<DateOnly>(type: "date", nullable: true),
                    payout_account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_accounts_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "savings",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_product_id",
                schema: "savings",
                table: "accounts",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_tenant_id",
                schema: "savings",
                table: "accounts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_tenant_id_account_number",
                schema: "savings",
                table: "accounts",
                columns: new[] { "tenant_id", "account_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_tenant_id_member_id",
                schema: "savings",
                table: "accounts",
                columns: new[] { "tenant_id", "member_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dividend_declarations_tenant_id",
                schema: "savings",
                table: "dividend_declarations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_dividend_declarations_tenant_id_financial_year",
                schema: "savings",
                table: "dividend_declarations",
                columns: new[] { "tenant_id", "financial_year" });

            migrationBuilder.CreateIndex(
                name: "ix_dividend_lines_declaration_id_member_id",
                schema: "savings",
                table: "dividend_lines",
                columns: new[] { "declaration_id", "member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id",
                schema: "savings",
                table: "products",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_code",
                schema: "savings",
                table: "products",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_withdrawal_requests_tenant_id",
                schema: "savings",
                table: "withdrawal_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_withdrawal_requests_tenant_id_account_number",
                schema: "savings",
                table: "withdrawal_requests",
                columns: new[] { "tenant_id", "account_number" });

            migrationBuilder.CreateIndex(
                name: "ix_withdrawal_requests_tenant_id_status",
                schema: "savings",
                table: "withdrawal_requests",
                columns: new[] { "tenant_id", "status" });

            // Tenant row-level security (defence in depth behind the EF query filters).
            migrationBuilder.EnableTenantIsolation("savings", "products");
            migrationBuilder.EnableTenantIsolation("savings", "accounts");
            migrationBuilder.EnableTenantIsolation("savings", "withdrawal_requests");
            migrationBuilder.EnableTenantIsolation("savings", "dividend_declarations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantIsolation("savings", "products");
            migrationBuilder.DisableTenantIsolation("savings", "accounts");
            migrationBuilder.DisableTenantIsolation("savings", "withdrawal_requests");
            migrationBuilder.DisableTenantIsolation("savings", "dividend_declarations");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "savings");

            migrationBuilder.DropTable(
                name: "dividend_lines",
                schema: "savings");

            migrationBuilder.DropTable(
                name: "withdrawal_requests",
                schema: "savings");

            migrationBuilder.DropTable(
                name: "products",
                schema: "savings");

            migrationBuilder.DropTable(
                name: "dividend_declarations",
                schema: "savings");
        }
    }
}
