using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sacco.Migrations.Platform
{
    /// <inheritdoc />
    public partial class TenantFavicon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "branding_favicon_url",
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
                name: "branding_favicon_url",
                schema: "platform",
                table: "tenants");
        }
    }
}
