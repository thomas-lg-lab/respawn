using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Games.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Games : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "games");

            migrationBuilder.CreateTable(
                name: "games",
                schema: "games",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    name_source_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    igdb_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_games", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "linked_accounts",
                schema: "games",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_linked_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "store_entries",
                schema: "games",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_store_entries_games_game_id",
                        column: x => x.game_id,
                        principalSchema: "games",
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "raw_snapshots",
                schema: "games",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    complete = table.Column<bool>(type: "boolean", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_raw_snapshots", x => x.id);
                    table.CheckConstraint("ck_raw_snapshots_personal", "(owner_id IS NULL) = (linked_account_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_raw_snapshots_linked_accounts_linked_account_id",
                        column: x => x.linked_account_id,
                        principalSchema: "games",
                        principalTable: "linked_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "observations",
                schema: "games",
                columns: table => new
                {
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    playtime_seconds = table.Column<long>(type: "bigint", nullable: true),
                    known_playtime_seconds = table.Column<long>(type: "bigint", nullable: true),
                    recent_playtime_seconds = table.Column<long>(type: "bigint", nullable: true),
                    last_played_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_observations", x => new { x.snapshot_id, x.store_entry_id });
                    table.CheckConstraint("ck_observations_playtime", "playtime_seconds >= 0 AND known_playtime_seconds >= 0 AND recent_playtime_seconds >= 0");
                    table.ForeignKey(
                        name: "fk_observations_raw_snapshots_snapshot_id",
                        column: x => x.snapshot_id,
                        principalSchema: "games",
                        principalTable: "raw_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_observations_store_entries_store_entry_id",
                        column: x => x.store_entry_id,
                        principalSchema: "games",
                        principalTable: "store_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ownerships",
                schema: "games",
                columns: table => new
                {
                    linked_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    playtime_seconds = table.Column<long>(type: "bigint", nullable: true),
                    last_played_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    as_of = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recent_playtime_seconds = table.Column<long>(type: "bigint", nullable: true),
                    unlisted_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ownerships", x => new { x.linked_account_id, x.store_entry_id });
                    table.CheckConstraint("ck_ownerships_playtime", "playtime_seconds >= 0 AND recent_playtime_seconds >= 0");
                    table.ForeignKey(
                        name: "fk_ownerships_linked_accounts_linked_account_id",
                        column: x => x.linked_account_id,
                        principalSchema: "games",
                        principalTable: "linked_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ownerships_raw_snapshots_snapshot_id",
                        column: x => x.snapshot_id,
                        principalSchema: "games",
                        principalTable: "raw_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ownerships_store_entries_store_entry_id",
                        column: x => x.store_entry_id,
                        principalSchema: "games",
                        principalTable: "store_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_games_igdb_id",
                schema: "games",
                table: "games",
                column: "igdb_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_linked_accounts_owner_id",
                schema: "games",
                table: "linked_accounts",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_linked_accounts_source_external_id",
                schema: "games",
                table: "linked_accounts",
                columns: new[] { "source", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_observations_store_entry_id",
                schema: "games",
                table: "observations",
                column: "store_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_ownerships_snapshot_id",
                schema: "games",
                table: "ownerships",
                column: "snapshot_id");

            migrationBuilder.CreateIndex(
                name: "ix_ownerships_store_entry_id",
                schema: "games",
                table: "ownerships",
                column: "store_entry_id");

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

            migrationBuilder.CreateIndex(
                name: "ix_store_entries_game_id",
                schema: "games",
                table: "store_entries",
                column: "game_id");

            migrationBuilder.CreateIndex(
                name: "ix_store_entries_source_external_id",
                schema: "games",
                table: "store_entries",
                columns: new[] { "source", "external_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "observations",
                schema: "games");

            migrationBuilder.DropTable(
                name: "ownerships",
                schema: "games");

            migrationBuilder.DropTable(
                name: "raw_snapshots",
                schema: "games");

            migrationBuilder.DropTable(
                name: "store_entries",
                schema: "games");

            migrationBuilder.DropTable(
                name: "linked_accounts",
                schema: "games");

            migrationBuilder.DropTable(
                name: "games",
                schema: "games");
        }
    }
}
