using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Games.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261128171838_StoreTagNamesReadAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "read_at",
            schema: "games",
            table: "store_tag_names",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "read_at",
            schema: "games",
            table: "store_tag_names");
    }
}
