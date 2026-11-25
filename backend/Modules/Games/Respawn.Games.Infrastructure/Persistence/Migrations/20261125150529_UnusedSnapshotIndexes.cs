using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Games.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261125150529_UnusedSnapshotIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_raw_snapshots_linked_account_id_kind_taken_at",
            schema: "games",
            table: "raw_snapshots");

        migrationBuilder.DropIndex(
            name: "ix_raw_snapshots_source_kind_taken_at",
            schema: "games",
            table: "raw_snapshots");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_raw_snapshots_linked_account_id_kind_taken_at",
            schema: "games",
            table: "raw_snapshots",
            columns: new[] { "linked_account_id", "kind", "taken_at" });

        migrationBuilder.CreateIndex(
            name: "ix_raw_snapshots_source_kind_taken_at",
            schema: "games",
            table: "raw_snapshots",
            columns: new[] { "source", "kind", "taken_at" });
    }
}
