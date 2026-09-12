using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Sacco.Shared.Persistence;

#nullable disable

namespace Sacco.Migrations.Ledger
{
    /// <inheritdoc />
    public partial class InitialLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ledger");

            migrationBuilder.CreateTable(
                name: "gl_accounts",
                schema: "ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    segment = table.Column<int>(type: "integer", nullable: false),
                    normal_balance = table.Column<int>(type: "integer", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_postable = table.Column<bool>(type: "boolean", nullable: false),
                    is_control_account = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    balance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gl_accounts", x => x.id);
                    table.CheckConstraint("ck_gl_accounts_segment", "segment IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_gl_accounts_gl_accounts_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "ledger",
                        principalTable: "gl_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_entries",
                schema: "ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    value_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    initiated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    initiated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reversal_of_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reversed_by_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_journal_entries_journal_entries_reversal_of_entry_id",
                        column: x => x.reversal_of_entry_id,
                        principalSchema: "ledger",
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_accounts",
                schema: "ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    control_gl_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    segment = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    product_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    balance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    held_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    opened_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_accounts", x => x.id);
                    table.CheckConstraint("ck_ledger_accounts_held_non_negative", "held_amount >= 0");
                    table.CheckConstraint("ck_ledger_accounts_segment", "segment IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_ledger_accounts_gl_accounts_control_gl_account_id",
                        column: x => x.control_gl_account_id,
                        principalSchema: "ledger",
                        principalTable: "gl_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_lines",
                schema: "ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    journal_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_number = table.Column<int>(type: "integer", nullable: false),
                    gl_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gl_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ledger_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ledger_account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    segment = table.Column<int>(type: "integer", nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    narrative = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_lines", x => x.id);
                    table.CheckConstraint("ck_journal_lines_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_journal_lines_segment", "segment IN (1, 2)");
                    table.ForeignKey(
                        name: "fk_journal_lines_gl_accounts_gl_account_id",
                        column: x => x.gl_account_id,
                        principalSchema: "ledger",
                        principalTable: "gl_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_lines_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalSchema: "ledger",
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_journal_lines_ledger_accounts_ledger_account_id",
                        column: x => x.ledger_account_id,
                        principalSchema: "ledger",
                        principalTable: "ledger_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gl_accounts_parent_id",
                schema: "ledger",
                table: "gl_accounts",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_gl_accounts_tenant_id",
                schema: "ledger",
                table: "gl_accounts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_gl_accounts_tenant_id_code",
                schema: "ledger",
                table: "gl_accounts",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_reversal_of_entry_id",
                schema: "ledger",
                table: "journal_entries",
                column: "reversal_of_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_tenant_id",
                schema: "ledger",
                table: "journal_entries",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_tenant_id_reference",
                schema: "ledger",
                table: "journal_entries",
                columns: new[] { "tenant_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_tenant_id_status",
                schema: "ledger",
                table: "journal_entries",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_tenant_id_value_date",
                schema: "ledger",
                table: "journal_entries",
                columns: new[] { "tenant_id", "value_date" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_gl_account_id",
                schema: "ledger",
                table: "journal_lines",
                column: "gl_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_journal_entry_id_line_number",
                schema: "ledger",
                table: "journal_lines",
                columns: new[] { "journal_entry_id", "line_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_ledger_account_id",
                schema: "ledger",
                table: "journal_lines",
                column: "ledger_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_accounts_control_gl_account_id",
                schema: "ledger",
                table: "ledger_accounts",
                column: "control_gl_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_accounts_tenant_id",
                schema: "ledger",
                table: "ledger_accounts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_accounts_tenant_id_account_number",
                schema: "ledger",
                table: "ledger_accounts",
                columns: new[] { "tenant_id", "account_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_accounts_tenant_id_member_id",
                schema: "ledger",
                table: "ledger_accounts",
                columns: new[] { "tenant_id", "member_id" });

            // Tenant row-level security (defence in depth behind the EF query filters).
            migrationBuilder.EnableTenantIsolation("ledger", "gl_accounts");
            migrationBuilder.EnableTenantIsolation("ledger", "ledger_accounts");
            migrationBuilder.EnableTenantIsolation("ledger", "journal_entries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantIsolation("ledger", "gl_accounts");
            migrationBuilder.DisableTenantIsolation("ledger", "ledger_accounts");
            migrationBuilder.DisableTenantIsolation("ledger", "journal_entries");

            migrationBuilder.DropTable(
                name: "journal_lines",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "journal_entries",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "ledger_accounts",
                schema: "ledger");

            migrationBuilder.DropTable(
                name: "gl_accounts",
                schema: "ledger");
        }
    }
}
