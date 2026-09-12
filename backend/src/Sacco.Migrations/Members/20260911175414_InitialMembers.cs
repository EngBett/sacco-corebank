using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Sacco.Shared.Persistence;

#nullable disable

namespace Sacco.Migrations.Members
{
    /// <inheritdoc />
    public partial class InitialMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "members");

            migrationBuilder.CreateTable(
                name: "member_number_sequences",
                schema: "members",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    next_value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_number_sequences", x => x.tenant_id);
                });

            migrationBuilder.CreateTable(
                name: "members",
                schema: "members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    details_first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    details_middle_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    details_last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    details_gender = table.Column<int>(type: "integer", nullable: false),
                    details_date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    details_national_id_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    details_kra_pin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    details_phone_number = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    details_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    details_postal_address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    details_county = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    details_occupation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    details_employer = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    details_employee_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    next_of_kin_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    next_of_kin_relationship = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    next_of_kin_phone_number = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    kyc_status = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    joined_at = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    registered_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kyc_verified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kyc_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    kyc_rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    suspension_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    suspended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    exited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_members", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "membership_applications",
                schema: "members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    details_first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    details_middle_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    details_last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    details_gender = table.Column<int>(type: "integer", nullable: false),
                    details_date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    details_national_id_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    details_kra_pin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    details_phone_number = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    details_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    details_postal_address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    details_county = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    details_occupation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    details_employer = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    details_employee_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    next_of_kin_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    next_of_kin_relationship = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    next_of_kin_phone_number = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    bot_check_passed = table.Column<bool>(type: "boolean", nullable: false),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_membership_applications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "kyc_documents",
                schema: "members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    file_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kyc_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_kyc_documents_members_member_id",
                        column: x => x.member_id,
                        principalSchema: "members",
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_kyc_documents_member_id",
                schema: "members",
                table: "kyc_documents",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_members_details_national_id_number",
                schema: "members",
                table: "members",
                column: "details_national_id_number");

            migrationBuilder.CreateIndex(
                name: "ix_members_details_phone_number",
                schema: "members",
                table: "members",
                column: "details_phone_number");

            migrationBuilder.CreateIndex(
                name: "ix_members_tenant_id",
                schema: "members",
                table: "members",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_members_tenant_id_kyc_status",
                schema: "members",
                table: "members",
                columns: new[] { "tenant_id", "kyc_status" });

            migrationBuilder.CreateIndex(
                name: "ix_members_tenant_id_member_number",
                schema: "members",
                table: "members",
                columns: new[] { "tenant_id", "member_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_membership_applications_details_national_id_number",
                schema: "members",
                table: "membership_applications",
                column: "details_national_id_number");

            migrationBuilder.CreateIndex(
                name: "ix_membership_applications_tenant_id",
                schema: "members",
                table: "membership_applications",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_membership_applications_tenant_id_status_submitted_at",
                schema: "members",
                table: "membership_applications",
                columns: new[] { "tenant_id", "status", "submitted_at" });

            // Tenant row-level security (defence in depth behind the EF query filters).
            migrationBuilder.EnableTenantIsolation("members", "members");
            migrationBuilder.EnableTenantIsolation("members", "membership_applications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DisableTenantIsolation("members", "members");
            migrationBuilder.DisableTenantIsolation("members", "membership_applications");

            migrationBuilder.DropTable(
                name: "kyc_documents",
                schema: "members");

            migrationBuilder.DropTable(
                name: "member_number_sequences",
                schema: "members");

            migrationBuilder.DropTable(
                name: "membership_applications",
                schema: "members");

            migrationBuilder.DropTable(
                name: "members",
                schema: "members");
        }
    }
}
