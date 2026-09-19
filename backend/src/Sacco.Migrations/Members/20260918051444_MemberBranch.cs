using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sacco.Migrations.Members
{
    /// <inheritdoc />
    public partial class MemberBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "members",
                table: "members",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_members_tenant_id_branch_id",
                schema: "members",
                table: "members",
                columns: new[] { "tenant_id", "branch_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_members_tenant_id_branch_id",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "members",
                table: "members");
        }
    }
}
