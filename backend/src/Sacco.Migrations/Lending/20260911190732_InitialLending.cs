using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Sacco.Shared.Persistence;

#nullable disable

namespace Sacco.Migrations.Lending
{
    /// <inheritdoc />
    public partial class InitialLending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "lending");

            migrationBuilder.CreateTable(
                name: "loan_number_sequences",
                schema: "lending",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    next_value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_number_sequences", x => x.tenant_id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "lending",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    segment = table.Column<int>(type: "integer", nullable: false),
                    control_gl_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    interest_income_gl_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    interest_receivable_gl_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fee_income_gl_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provision_gl_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provision_expense_gl_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    interest_rate_bps = table.Column<int>(type: "integer", nullable: false),
                    interest_method = table.Column<int>(type: "integer", nullable: false),
                    min_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    max_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    min_term_months = table.Column<int>(type: "integer", nullable: false),
                    max_term_months = table.Column<int>(type: "integer", nullable: false),
                    deposit_multiplier = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    min_membership_months = table.Column<int>(type: "integer", nullable: false),
                    processing_fee_bps = table.Column<int>(type: "integer", nullable: false),
                    requires_guarantors = table.Column<bool>(type: "boolean", nullable: false),
                    min_guarantors = table.Column<int>(type: "integer", nullable: false),
                    committee_threshold = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    committee_approvals_required = table.Column<int>(type: "integer", nullable: false),
                    grace_period_days = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "provisioning_config",
                schema: "lending",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provisioning_config", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "provisioning_runs",
                schema: "lending",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    as_of = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    computed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    total_outstanding = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    total_provision_required = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provisioning_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "loans",
                schema: "lending",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    segment = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    term_months = table.Column<int>(type: "integer", nullable: false),
                    interest_rate_bps = table.Column<int>(type: "integer", nullable: false),
                    interest_method = table.Column<int>(type: "integer", nullable: false),
                    purpose = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    eligibility_bosa_deposits = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    eligibility_shares = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    eligibility_deposit_multiplier = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    eligibility_max_eligible_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    eligibility_membership_months = table.Column<int>(type: "integer", nullable: false),
                    eligibility_contribution_months = table.Column<int>(type: "integer", nullable: false),
                    eligibility_existing_outstanding = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    disbursement_account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    pledged_deposits_account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    pledged_deposits_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ledger_account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    applied_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appraised_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    appraised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    appraisal_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disbursed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disbursement_date = table.Column<DateOnly>(type: "date", nullable: true),
                    disbursed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    processing_fee = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loans", x => x.id);
                    table.ForeignKey(
                        name: "fk_loans_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "lending",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "aging_buckets",
                schema: "lending",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    min_days_in_arrears = table.Column<int>(type: "integer", nullable: false),
                    provision_rate_bps = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aging_buckets", x => x.id);
                    table.ForeignKey(
                        name: "fk_aging_buckets_provisioning_config_config_id",
                        column: x => x.config_id,
                        principalSchema: "lending",
                        principalTable: "provisioning_config",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "provisioning_lines",
                schema: "lending",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    segment = table.Column<int>(type: "integer", nullable: false),
                    days_in_arrears = table.Column<int>(type: "integer", nullable: false),
                    bucket = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provision_rate_bps = table.Column<int>(type: "integer", nullable: false),
                    outstanding_principal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    arrears_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    provision_required = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provisioning_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_provisioning_lines_provisioning_runs_run_id",
                        column: x => x.run_id,
                        principalSchema: "lending",
                        principalTable: "provisioning_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "loan_approvals",
                schema: "lending",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approver_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_approvals", x => x.id);
                    table.ForeignKey(
                        name: "fk_loan_approvals_loans_loan_id",
                        column: x => x.loan_id,
                        principalSchema: "lending",
                        principalTable: "loans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "loan_guarantors",
                schema: "lending",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guarantor_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deposits_account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount_guaranteed = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_guarantors", x => x.id);
                    table.ForeignKey(
                        name: "fk_loan_guarantors_loans_loan_id",
                        column: x => x.loan_id,
                        principalSchema: "lending",
                        principalTable: "loans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "repayment_schedule",
                schema: "lending",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    principal_due = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    interest_due = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    principal_paid = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    interest_paid = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    interest_accrued = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repayment_schedule", x => x.id);
                    table.ForeignKey(
                        name: "fk_repayment_schedule_loans_loan_id",
                        column: x => x.loan_id,
                        principalSchema: "lending",
                        principalTable: "loans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_aging_buckets_config_id",
                schema: "lending",
                table: "aging_buckets",
                column: "config_id");

            migrationBuilder.CreateIndex(
                name: "ix_loan_approvals_loan_id_approver_user_id",
                schema: "lending",
                table: "loan_approvals",
                columns: new[] { "loan_id", "approver_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loan_guarantors_guarantor_member_id",
                schema: "lending",
                table: "loan_guarantors",
                column: "guarantor_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_loan_guarantors_loan_id",
                schema: "lending",
                table: "loan_guarantors",
                column: "loan_id");

            migrationBuilder.CreateIndex(
                name: "ix_loans_product_id",
                schema: "lending",
                table: "loans",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_loans_tenant_id",
                schema: "lending",
                table: "loans",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_loans_tenant_id_loan_number",
                schema: "lending",
                table: "loans",
                columns: new[] { "tenant_id", "loan_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loans_tenant_id_member_id",
                schema: "lending",
                table: "loans",
                columns: new[] { "tenant_id", "member_id" });

            migrationBuilder.CreateIndex(
                name: "ix_loans_tenant_id_status",
                schema: "lending",
                table: "loans",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id",
                schema: "lending",
                table: "products",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_code",
                schema: "lending",
                table: "products",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provisioning_config_tenant_id",
                schema: "lending",
                table: "provisioning_config",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provisioning_lines_run_id",
                schema: "lending",
                table: "provisioning_lines",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_provisioning_runs_tenant_id",
                schema: "lending",
                table: "provisioning_runs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_provisioning_runs_tenant_id_as_of",
                schema: "lending",
                table: "provisioning_runs",
                columns: new[] { "tenant_id", "as_of" });

            migrationBuilder.CreateIndex(
                name: "ix_repayment_schedule_due_date_status",
                schema: "lending",
                table: "repayment_schedule",
                columns: new[] { "due_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_repayment_schedule_loan_id_number",
                schema: "lending",
                table: "repayment_schedule",
                columns: new[] { "loan_id", "number" },
                unique: true);

            // Tenant row-level security (defence in depth behind the EF query filters).
            migrationBuilder.EnableTenantIsolation("lending", "products");
            migrationBuilder.EnableTenantIsolation("lending", "loans");
            migrationBuilder.EnableTenantIsolation("lending", "provisioning_config");
            migrationBuilder.EnableTenantIsolation("lending", "provisioning_runs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantIsolation("lending", "products");
            migrationBuilder.DisableTenantIsolation("lending", "loans");
            migrationBuilder.DisableTenantIsolation("lending", "provisioning_config");
            migrationBuilder.DisableTenantIsolation("lending", "provisioning_runs");

            migrationBuilder.DropTable(
                name: "aging_buckets",
                schema: "lending");

            migrationBuilder.DropTable(
                name: "loan_approvals",
                schema: "lending");

            migrationBuilder.DropTable(
                name: "loan_guarantors",
                schema: "lending");

            migrationBuilder.DropTable(
                name: "loan_number_sequences",
                schema: "lending");

            migrationBuilder.DropTable(
                name: "provisioning_lines",
                schema: "lending");

            migrationBuilder.DropTable(
                name: "repayment_schedule",
                schema: "lending");

            migrationBuilder.DropTable(
                name: "provisioning_config",
                schema: "lending");

            migrationBuilder.DropTable(
                name: "provisioning_runs",
                schema: "lending");

            migrationBuilder.DropTable(
                name: "loans",
                schema: "lending");

            migrationBuilder.DropTable(
                name: "products",
                schema: "lending");
        }
    }
}
