using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sacco.Migrations.Platform
{
    /// <summary>
    /// Audit entries written before outcomes existed were given an empty outcome, which is not a value the enum can mean —
    /// it hid every one of them from the outcome filter. They all record something that happened, so they are successes.
    /// The table is append-only (ADR 0019), so the trigger is lifted for this one repair and put straight back.
    /// </summary>
    public partial class RepairLegacyAuditOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE platform.audit_log DISABLE TRIGGER audit_log_append_only;
                ALTER TABLE platform.audit_log NO FORCE ROW LEVEL SECURITY;
                UPDATE platform.audit_log SET outcome = 'Success' WHERE outcome = '' AND hash = '';
                ALTER TABLE platform.audit_log FORCE ROW LEVEL SECURITY;
                ALTER TABLE platform.audit_log ENABLE TRIGGER audit_log_append_only;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the previous value carried no meaning.
        }
    }
}
