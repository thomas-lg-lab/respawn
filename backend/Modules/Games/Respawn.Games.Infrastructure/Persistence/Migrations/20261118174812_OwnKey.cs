using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Games.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OwnKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "own_credential",
                schema: "games",
                table: "sync_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "credential",
                schema: "games",
                table: "linked_accounts",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "credential_set_at",
                schema: "games",
                table: "linked_accounts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "own_credential",
                schema: "games",
                table: "sync_runs");

            migrationBuilder.DropColumn(
                name: "credential",
                schema: "games",
                table: "linked_accounts");

            migrationBuilder.DropColumn(
                name: "credential_set_at",
                schema: "games",
                table: "linked_accounts");
        }
    }
}
