using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Accounts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InterfaceSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "interface_character",
                schema: "accounts",
                table: "users",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "interface_language",
                schema: "accounts",
                table: "users",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "interface_theme",
                schema: "accounts",
                table: "users",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_interface_character",
                schema: "accounts",
                table: "users",
                sql: "interface_character IN ('pixel', 'plain')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_interface_language",
                schema: "accounts",
                table: "users",
                sql: "interface_language IN ('fr', 'en')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_interface_settings_together",
                schema: "accounts",
                table: "users",
                sql: "(interface_language IS NULL) = (interface_theme IS NULL) AND (interface_theme IS NULL) = (interface_character IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_interface_theme",
                schema: "accounts",
                table: "users",
                sql: "interface_theme IN ('system', 'dark', 'light')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_interface_character",
                schema: "accounts",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_interface_language",
                schema: "accounts",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_interface_settings_together",
                schema: "accounts",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_interface_theme",
                schema: "accounts",
                table: "users");

            migrationBuilder.DropColumn(
                name: "interface_character",
                schema: "accounts",
                table: "users");

            migrationBuilder.DropColumn(
                name: "interface_language",
                schema: "accounts",
                table: "users");

            migrationBuilder.DropColumn(
                name: "interface_theme",
                schema: "accounts",
                table: "users");
        }
    }
}
