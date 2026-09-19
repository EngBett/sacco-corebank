using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Sacco.Shared.Persistence;

#nullable disable

namespace Sacco.Migrations.Savings
{
    /// <inheritdoc />
    public partial class ShareMarketplace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "share_listings",
                schema: "savings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_shares_account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    seller_fosa_account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    listed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    buyer_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    buyer_shares_account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    buyer_fosa_account_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    claimed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    journal_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_share_listings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_share_listings_tenant_id",
                schema: "savings",
                table: "share_listings",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_share_listings_tenant_id_buyer_member_id",
                schema: "savings",
                table: "share_listings",
                columns: new[] { "tenant_id", "buyer_member_id" });

            migrationBuilder.CreateIndex(
                name: "ix_share_listings_tenant_id_seller_member_id",
                schema: "savings",
                table: "share_listings",
                columns: new[] { "tenant_id", "seller_member_id" });

            migrationBuilder.CreateIndex(
                name: "ix_share_listings_tenant_id_status",
                schema: "savings",
                table: "share_listings",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.EnableTenantIsolation("savings", "share_listings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "share_listings",
                schema: "savings");
        }
    }
}
