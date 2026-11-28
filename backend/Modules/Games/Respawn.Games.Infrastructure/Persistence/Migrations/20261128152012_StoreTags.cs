using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Games.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261128152012_StoreTags : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "tags_asked_at",
            schema: "games",
            table: "store_entries",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "store_entry_tags",
            schema: "games",
            columns: table => new
            {
                store_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                tag_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                weight = table.Column<int>(type: "integer", nullable: false),
                rank = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_store_entry_tags", x => new { x.store_entry_id, x.tag_id });
                table.CheckConstraint("ck_store_entry_tags_rank", "rank >= 0");
                table.ForeignKey(
                    name: "fk_store_entry_tags_store_entries_store_entry_id",
                    column: x => x.store_entry_id,
                    principalSchema: "games",
                    principalTable: "store_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "store_tag_names",
            schema: "games",
            columns: table => new
            {
                source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                tag_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_store_tag_names", x => new { x.source, x.tag_id, x.language });
            });

        migrationBuilder.CreateIndex(
            name: "ix_store_entries_source_tags_asked_at",
            schema: "games",
            table: "store_entries",
            columns: new[] { "source", "tags_asked_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "store_entry_tags",
            schema: "games");

        migrationBuilder.DropTable(
            name: "store_tag_names",
            schema: "games");

        migrationBuilder.DropIndex(
            name: "ix_store_entries_source_tags_asked_at",
            schema: "games",
            table: "store_entries");

        migrationBuilder.DropColumn(
            name: "tags_asked_at",
            schema: "games",
            table: "store_entries");
    }
}
