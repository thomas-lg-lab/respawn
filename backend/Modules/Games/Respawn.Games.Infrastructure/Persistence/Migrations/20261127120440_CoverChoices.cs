using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Games.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261127120440_CoverChoices : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(
            name: "pk_images",
            schema: "games",
            table: "images");

        migrationBuilder.AddColumn<string>(
            name: "slot",
            schema: "games",
            table: "images",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddPrimaryKey(
            name: "pk_images",
            schema: "games",
            table: "images",
            columns: new[] { "game_id", "kind", "slot" });

        migrationBuilder.CreateTable(
            name: "cover_choices",
            schema: "games",
            columns: table => new
            {
                owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                game_id = table.Column<Guid>(type: "uuid", nullable: false),
                source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                chosen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_cover_choices", x => new { x.owner_id, x.game_id });
                table.ForeignKey(
                    name: "fk_cover_choices_games_game_id",
                    column: x => x.game_id,
                    principalSchema: "games",
                    principalTable: "games",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_cover_choices_game_id",
            schema: "games",
            table: "cover_choices",
            column: "game_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "cover_choices",
            schema: "games");

        migrationBuilder.DropPrimaryKey(
            name: "pk_images",
            schema: "games",
            table: "images");

        migrationBuilder.DropColumn(
            name: "slot",
            schema: "games",
            table: "images");

        migrationBuilder.AddPrimaryKey(
            name: "pk_images",
            schema: "games",
            table: "images",
            columns: new[] { "game_id", "kind" });
    }
}
