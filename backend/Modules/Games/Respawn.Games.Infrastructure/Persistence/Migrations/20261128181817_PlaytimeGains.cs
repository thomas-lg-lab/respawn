using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Games.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261128181817_PlaytimeGains : Migration
{
    /// <summary>Written by hand: the gains of every reading kept so far, by the rule the store writes them
    /// with at each reading (EfLibraryStore.GainsSince), over each account's whole history. On an empty
    /// table.</summary>
    internal const string Sql =
        """
        SET LOCAL jit = off;
        WITH readings AS (
            SELECT s.linked_account_id,
                   s.id AS snapshot_id,
                   o.store_entry_id,
                   s.taken_at,
                   o.last_played_at,
                   o.known_playtime_seconds AS known,
                   LAG(o.known_playtime_seconds) OVER reading AS before,
                   LAG(s.taken_at) OVER reading AS before_at
            FROM games.raw_snapshots s
            JOIN games.observations o ON o.snapshot_id = s.id
            WHERE s.linked_account_id IS NOT NULL
              AND o.known_playtime_seconds IS NOT NULL
            WINDOW reading AS (PARTITION BY s.linked_account_id, o.store_entry_id ORDER BY s.taken_at, s.id)
        )
        INSERT INTO games.playtime_gains (snapshot_id, store_entry_id, linked_account_id, day, seconds)
        SELECT r.snapshot_id,
               r.store_entry_id,
               r.linked_account_id,
               (CASE WHEN r.last_played_at > r.before_at AND r.last_played_at <= r.taken_at
                     THEN r.last_played_at
                     ELSE r.taken_at
                END AT TIME ZONE 'UTC')::date,
               r.known - r.before
        FROM readings r
        WHERE r.before IS NOT NULL AND r.known > r.before;
        """;

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "playtime_gains",
            schema: "games",
            columns: table => new
            {
                snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                store_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                linked_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                day = table.Column<DateOnly>(type: "date", nullable: false),
                seconds = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_playtime_gains", x => new { x.snapshot_id, x.store_entry_id });
                table.CheckConstraint("ck_playtime_gains_seconds", "seconds > 0");
                table.ForeignKey(
                    name: "fk_playtime_gains_linked_accounts_linked_account_id",
                    column: x => x.linked_account_id,
                    principalSchema: "games",
                    principalTable: "linked_accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_playtime_gains_raw_snapshots_snapshot_id",
                    column: x => x.snapshot_id,
                    principalSchema: "games",
                    principalTable: "raw_snapshots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_playtime_gains_store_entries_store_entry_id",
                    column: x => x.store_entry_id,
                    principalSchema: "games",
                    principalTable: "store_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_playtime_gains_linked_account_id_day",
            schema: "games",
            table: "playtime_gains",
            columns: new[] { "linked_account_id", "day" });

        migrationBuilder.CreateIndex(
            name: "ix_playtime_gains_store_entry_id_day",
            schema: "games",
            table: "playtime_gains",
            columns: new[] { "store_entry_id", "day" });

        migrationBuilder.Sql(Sql);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "playtime_gains",
            schema: "games");
    }
}
