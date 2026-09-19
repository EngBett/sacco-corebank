using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Sacco.Shared.Persistence;

#nullable disable

namespace Sacco.Migrations.Savings
{
    /// <inheritdoc />
    public partial class FeeMatrixAndBalanceEnquiries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fee_gl_account_code",
                schema: "savings",
                table: "withdrawal_requests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "fee_rule_id",
                schema: "savings",
                table: "withdrawal_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "fee_segment",
                schema: "savings",
                table: "withdrawal_requests",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "balance_enquiries",
                schema: "savings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    fee_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    charged_account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    journal_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    visible_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_balance_enquiries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fee_rules",
                schema: "savings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_type = table.Column<int>(type: "integer", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: true),
                    product_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    min_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    max_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    charge_type = table.Column<int>(type: "integer", nullable: false),
                    fixed_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    rate_bps = table.Column<int>(type: "integer", nullable: true),
                    min_charge = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    max_charge = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    fee_income_gl_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fee_income_segment = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    supersedes_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    deactivated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fee_tiers",
                schema: "savings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    up_to = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    charge = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_tiers", x => x.id);
                    table.ForeignKey(
                        name: "fk_fee_tiers_fee_rules_rule_id",
                        column: x => x.rule_id,
                        principalSchema: "savings",
                        principalTable: "fee_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_balance_enquiries_tenant_id",
                schema: "savings",
                table: "balance_enquiries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_balance_enquiries_tenant_id_member_id_idempotency_key",
                schema: "savings",
                table: "balance_enquiries",
                columns: new[] { "tenant_id", "member_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_balance_enquiries_tenant_id_member_id_visible_until",
                schema: "savings",
                table: "balance_enquiries",
                columns: new[] { "tenant_id", "member_id", "visible_until" });

            migrationBuilder.CreateIndex(
                name: "ix_fee_rules_tenant_id",
                schema: "savings",
                table: "fee_rules",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_fee_rules_tenant_id_status_transaction_type",
                schema: "savings",
                table: "fee_rules",
                columns: new[] { "tenant_id", "status", "transaction_type" });

            migrationBuilder.CreateIndex(
                name: "ix_fee_tiers_rule_id",
                schema: "savings",
                table: "fee_tiers",
                column: "rule_id");

            migrationBuilder.EnableTenantIsolation("savings", "fee_rules");
            migrationBuilder.EnableTenantIsolation("savings", "balance_enquiries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "balance_enquiries",
                schema: "savings");

            migrationBuilder.DropTable(
                name: "fee_tiers",
                schema: "savings");

            migrationBuilder.DropTable(
                name: "fee_rules",
                schema: "savings");

            migrationBuilder.DropColumn(
                name: "fee_gl_account_code",
                schema: "savings",
                table: "withdrawal_requests");

            migrationBuilder.DropColumn(
                name: "fee_rule_id",
                schema: "savings",
                table: "withdrawal_requests");

            migrationBuilder.DropColumn(
                name: "fee_segment",
                schema: "savings",
                table: "withdrawal_requests");
        }
    }
}
