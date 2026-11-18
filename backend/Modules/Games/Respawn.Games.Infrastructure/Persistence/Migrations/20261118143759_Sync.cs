using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Games.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_raw_snapshots_personal",
                schema: "games",
                table: "raw_snapshots");

            migrationBuilder.DropColumn(
                name: "owner_id",
                schema: "games",
                table: "raw_snapshots");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "missing_since",
                schema: "games",
                table: "ownerships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sync_runs",
                schema: "games",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    error = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_runs", x => x.id);
                    table.CheckConstraint("ck_sync_runs_error", "(outcome = 'failed') = (error IS NOT NULL)");
                    table.CheckConstraint("ck_sync_runs_outcome", "outcome IN ('succeeded', 'nothing_visible', 'failed')");
                    table.CheckConstraint("ck_sync_runs_trigger", "trigger IN ('scheduled', 'manual')");
                    table.ForeignKey(
                        name: "fk_sync_runs_linked_accounts_linked_account_id",
                        column: x => x.linked_account_id,
                        principalSchema: "games",
                        principalTable: "linked_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sync_runs_raw_snapshots_snapshot_id",
                        column: x => x.snapshot_id,
                        principalSchema: "games",
                        principalTable: "raw_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sync_runs_linked_account_id_started_at",
                schema: "games",
                table: "sync_runs",
                columns: new[] { "linked_account_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_runs_snapshot_id",
                schema: "games",
                table: "sync_runs",
                column: "snapshot_id");

            migrationBuilder.CreateIndex(
                name: "ix_sync_runs_started_at",
                schema: "games",
                table: "sync_runs",
                column: "started_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_runs",
                schema: "games");

            migrationBuilder.DropColumn(
                name: "missing_since",
                schema: "games",
                table: "ownerships");

            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                schema: "games",
                table: "raw_snapshots",
                type: "uuid",
                nullable: true);

            // Written by hand: the column comes back with what it held, or the constraint below
            // refuses every personal snapshot.
            migrationBuilder.Sql(
                "UPDATE games.raw_snapshots AS s SET owner_id = a.owner_id FROM games.linked_accounts AS a WHERE a.id = s.linked_account_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_raw_snapshots_personal",
                schema: "games",
                table: "raw_snapshots",
                sql: "(owner_id IS NULL) = (linked_account_id IS NULL)");
        }
    }
}
