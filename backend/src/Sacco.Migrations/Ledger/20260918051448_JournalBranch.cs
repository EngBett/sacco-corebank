using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sacco.Migrations.Ledger
{
    /// <inheritdoc />
    public partial class JournalBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "ledger",
                table: "journal_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_tenant_id_branch_id",
                schema: "ledger",
                table: "journal_entries",
                columns: new[] { "tenant_id", "branch_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_journal_entries_tenant_id_branch_id",
                schema: "ledger",
                table: "journal_entries");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "ledger",
                table: "journal_entries");
        }
    }
}
