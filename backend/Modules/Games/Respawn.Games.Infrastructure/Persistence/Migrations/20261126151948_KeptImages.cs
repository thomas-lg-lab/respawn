using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Games.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261126151948_KeptImages : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "images",
            schema: "games",
            columns: table => new
            {
                game_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                content_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_images", x => new { x.game_id, x.kind });
                table.ForeignKey(
                    name: "fk_images_games_game_id",
                    column: x => x.game_id,
                    principalSchema: "games",
                    principalTable: "games",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_images_kind_source_key",
            schema: "games",
            table: "images",
            columns: new[] { "kind", "source", "key" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "images",
            schema: "games");
    }
}
