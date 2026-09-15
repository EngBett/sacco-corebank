using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sacco.Migrations.Members
{
    /// <inheritdoc />
    public partial class MemberExit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "exit_approved_by_user_id",
                schema: "members",
                table: "members",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "exit_reason",
                schema: "members",
                table: "members",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "exit_requested_at",
                schema: "members",
                table: "members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "exit_requested_by_user_id",
                schema: "members",
                table: "members",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "exit_settlement_json",
                schema: "members",
                table: "members",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "exit_approved_by_user_id",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "exit_reason",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "exit_requested_at",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "exit_requested_by_user_id",
                schema: "members",
                table: "members");

            migrationBuilder.DropColumn(
                name: "exit_settlement_json",
                schema: "members",
                table: "members");
        }
    }
}
