using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sacco.Migrations.Lending
{
    /// <inheritdoc />
    public partial class LoanProductListingsAndCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "category",
                schema: "lending",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "listing_amount_note",
                schema: "lending",
                table: "products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "listing_application_form_url",
                schema: "lending",
                table: "products",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "listing_display_order",
                schema: "lending",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<List<string>>(
                name: "listing_features",
                schema: "lending",
                table: "products",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<List<string>>(
                name: "listing_requirements",
                schema: "lending",
                table: "products",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<bool>(
                name: "listing_show_on_public_site",
                schema: "lending",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Existing loan products are listed under their segment (FOSA/BOSA); MSME is chosen explicitly later. Tenant isolation
            // is FORCEd on existing databases, so lift it for this one cross-tenant statement and restore it.
            migrationBuilder.Sql("""
                ALTER TABLE lending.products NO FORCE ROW LEVEL SECURITY;
                UPDATE lending.products SET category = segment WHERE category = 0;
                ALTER TABLE lending.products FORCE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "category",
                schema: "lending",
                table: "products");

            migrationBuilder.DropColumn(
                name: "listing_amount_note",
                schema: "lending",
                table: "products");

            migrationBuilder.DropColumn(
                name: "listing_application_form_url",
                schema: "lending",
                table: "products");

            migrationBuilder.DropColumn(
                name: "listing_display_order",
                schema: "lending",
                table: "products");

            migrationBuilder.DropColumn(
                name: "listing_features",
                schema: "lending",
                table: "products");

            migrationBuilder.DropColumn(
                name: "listing_requirements",
                schema: "lending",
                table: "products");

            migrationBuilder.DropColumn(
                name: "listing_show_on_public_site",
                schema: "lending",
                table: "products");
        }
    }
}
