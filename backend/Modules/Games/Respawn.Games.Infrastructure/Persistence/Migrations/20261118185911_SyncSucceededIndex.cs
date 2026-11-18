using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Games.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncSucceededIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_sync_runs_linked_account_id_started_at_succeeded",
                schema: "games",
                table: "sync_runs",
                columns: new[] { "linked_account_id", "started_at" },
                filter: "outcome = 'succeeded'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sync_runs_linked_account_id_started_at_succeeded",
                schema: "games",
                table: "sync_runs");
        }
    }
}
