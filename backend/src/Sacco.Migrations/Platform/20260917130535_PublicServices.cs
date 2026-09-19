using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Sacco.Shared.Persistence;

#nullable disable

namespace Sacco.Migrations.Platform
{
    /// <inheritdoc />
    public partial class PublicServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "public_services",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    icon = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_public_services", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_public_services_tenant_id",
                schema: "platform",
                table: "public_services",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_public_services_tenant_id_display_order",
                schema: "platform",
                table: "public_services",
                columns: new[] { "tenant_id", "display_order" });

            migrationBuilder.EnableTenantIsolation("platform", "public_services");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "public_services",
                schema: "platform");
        }
    }
}
