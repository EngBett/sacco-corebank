using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sacco.Migrations.Savings
{
    /// <inheritdoc />
    public partial class SavingsProductListings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "listing_amount_note",
                schema: "savings",
                table: "products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "listing_application_form_url",
                schema: "savings",
                table: "products",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "listing_display_order",
                schema: "savings",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<List<string>>(
                name: "listing_features",
                schema: "savings",
                table: "products",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<List<string>>(
                name: "listing_requirements",
                schema: "savings",
                table: "products",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<bool>(
                name: "listing_show_on_public_site",
                schema: "savings",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "listing_amount_note",
                schema: "savings",
                table: "products");

            migrationBuilder.DropColumn(
                name: "listing_application_form_url",
                schema: "savings",
                table: "products");

            migrationBuilder.DropColumn(
                name: "listing_display_order",
                schema: "savings",
                table: "products");

            migrationBuilder.DropColumn(
                name: "listing_features",
                schema: "savings",
                table: "products");

            migrationBuilder.DropColumn(
                name: "listing_requirements",
                schema: "savings",
                table: "products");

            migrationBuilder.DropColumn(
                name: "listing_show_on_public_site",
                schema: "savings",
                table: "products");
        }
    }
}
