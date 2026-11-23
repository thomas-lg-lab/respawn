using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Games.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotsByDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_raw_snapshots_linked_account_id_taken_at_id",
                schema: "games",
                table: "raw_snapshots",
                columns: new[] { "linked_account_id", "taken_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_raw_snapshots_linked_account_id_taken_at_id",
                schema: "games",
                table: "raw_snapshots");
        }
    }
}
