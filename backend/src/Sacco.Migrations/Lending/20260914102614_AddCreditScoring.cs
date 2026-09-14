using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Sacco.Shared.Persistence;

#nullable disable

namespace Sacco.Migrations.Lending
{
    /// <inheritdoc />
    public partial class AddCreditScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "loan_credit_scores",
                schema: "lending",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    loan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    grade = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    recommendation = table.Column<int>(type: "integer", nullable: false),
                    recommendation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    bureau_status = table.Column<int>(type: "integer", nullable: false),
                    bureau_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    bureau_narrative = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    factors_json = table.Column<string>(type: "jsonb", nullable: false),
                    scorecard_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    computed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_credit_scores", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scorecards",
                schema: "lending",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    approve_threshold = table.Column<int>(type: "integer", nullable: false),
                    refer_threshold = table.Column<int>(type: "integer", nullable: false),
                    decline_if_bureau_listed = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scorecards", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scorecard_factors",
                schema: "lending",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scorecard_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    max_points = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scorecard_factors", x => x.id);
                    table.ForeignKey(
                        name: "fk_scorecard_factors_scorecards_scorecard_id",
                        column: x => x.scorecard_id,
                        principalSchema: "lending",
                        principalTable: "scorecards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_loan_credit_scores_loan_id_computed_at",
                schema: "lending",
                table: "loan_credit_scores",
                columns: new[] { "loan_id", "computed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_loan_credit_scores_tenant_id",
                schema: "lending",
                table: "loan_credit_scores",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_scorecard_factors_scorecard_id_key",
                schema: "lending",
                table: "scorecard_factors",
                columns: new[] { "scorecard_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scorecards_tenant_id",
                schema: "lending",
                table: "scorecards",
                column: "tenant_id",
                unique: true);
            migrationBuilder.EnableTenantIsolation("lending", "scorecards");
            migrationBuilder.EnableTenantIsolation("lending", "loan_credit_scores");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loan_credit_scores",
                schema: "lending");

            migrationBuilder.DropTable(
                name: "scorecard_factors",
                schema: "lending");

            migrationBuilder.DropTable(
                name: "scorecards",
                schema: "lending");
        }
    }
}
