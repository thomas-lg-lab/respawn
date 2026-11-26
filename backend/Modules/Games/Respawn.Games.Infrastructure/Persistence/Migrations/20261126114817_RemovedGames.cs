using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Games.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261126114817_RemovedGames : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "listed_again_since",
            schema: "games",
            table: "ownerships",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "notice_dismissed_at",
            schema: "games",
            table: "ownerships",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_ownerships_listed_again",
            schema: "games",
            table: "ownerships",
            sql: "unlisted_since IS NOT NULL OR (listed_again_since IS NULL AND notice_dismissed_at IS NULL)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_ownerships_listed_again",
            schema: "games",
            table: "ownerships");

        migrationBuilder.DropColumn(
            name: "listed_again_since",
            schema: "games",
            table: "ownerships");

        migrationBuilder.DropColumn(
            name: "notice_dismissed_at",
            schema: "games",
            table: "ownerships");
    }
}
