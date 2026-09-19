using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sacco.Migrations.Platform
{
    /// <inheritdoc />
    public partial class TenantModeLogos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "branding_dark_mode_logo_url",
                schema: "platform",
                table: "tenants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "branding_light_mode_logo_url",
                schema: "platform",
                table: "tenants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "branding_dark_mode_logo_url",
                schema: "platform",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "branding_light_mode_logo_url",
                schema: "platform",
                table: "tenants");
        }
    }
}
