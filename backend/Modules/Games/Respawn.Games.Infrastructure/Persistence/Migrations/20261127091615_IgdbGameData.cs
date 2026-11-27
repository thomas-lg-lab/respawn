using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Games.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261127091615_IgdbGameData : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "igdb_asked_at",
            schema: "games",
            table: "store_entries",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "igdb_id",
            schema: "games",
            table: "store_entries",
            type: "bigint",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "igdb_games",
            schema: "games",
            columns: table => new
            {
                igdb_id = table.Column<long>(type: "bigint", nullable: false),
                name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                cover_image_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                genres = table.Column<List<string>>(type: "text[]", nullable: false),
                themes = table.Column<List<string>>(type: "text[]", nullable: false),
                summary = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                hastily_seconds = table.Column<int>(type: "integer", nullable: true),
                normally_seconds = table.Column<int>(type: "integer", nullable: true),
                completely_seconds = table.Column<int>(type: "integer", nullable: true),
                time_to_beat_count = table.Column<int>(type: "integer", nullable: true),
                read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_igdb_games", x => x.igdb_id);
                table.CheckConstraint("ck_igdb_games_time_to_beat", "hastily_seconds >= 0 AND normally_seconds >= 0 AND completely_seconds >= 0 AND time_to_beat_count >= 0");
            });

        migrationBuilder.CreateIndex(
            name: "ix_store_entries_source_igdb_asked_at",
            schema: "games",
            table: "store_entries",
            columns: new[] { "source", "igdb_asked_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "igdb_games",
            schema: "games");

        migrationBuilder.DropIndex(
            name: "ix_store_entries_source_igdb_asked_at",
            schema: "games",
            table: "store_entries");

        migrationBuilder.DropColumn(
            name: "igdb_asked_at",
            schema: "games",
            table: "store_entries");

        migrationBuilder.DropColumn(
            name: "igdb_id",
            schema: "games",
            table: "store_entries");
    }
}
