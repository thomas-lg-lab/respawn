using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Games.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261127230333_IgdbReleaseAndSimilar : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "released_on",
            schema: "games",
            table: "igdb_games",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<List<long>>(
            name: "similar",
            schema: "games",
            table: "igdb_games",
            type: "bigint[]",
            nullable: false,
            defaultValueSql: "'{}'");

        // The games IGDB was asked about before it was asked for these two are due again at its next
        // turn, a few at a time: dated long ago rather than never asked, which a cover would wait on.
        migrationBuilder.Sql(
            "UPDATE games.store_entries SET igdb_asked_at = TIMESTAMPTZ '2000-01-01 00:00:00+00' WHERE igdb_id IS NOT NULL;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "released_on",
            schema: "games",
            table: "igdb_games");

        migrationBuilder.DropColumn(
            name: "similar",
            schema: "games",
            table: "igdb_games");
    }
}
