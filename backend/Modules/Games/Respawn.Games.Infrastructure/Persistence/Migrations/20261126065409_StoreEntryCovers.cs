using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Games.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261126065409_StoreEntryCovers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "cover_asked_at",
            schema: "games",
            table: "store_entries",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "cover_path",
            schema: "games",
            table: "store_entries",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "cover_asked_at",
            schema: "games",
            table: "store_entries");

        migrationBuilder.DropColumn(
            name: "cover_path",
            schema: "games",
            table: "store_entries");
    }
}
