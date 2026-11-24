using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Accounts.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261124214209_HomeLayoutSetting : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_users_interface_settings_together",
            schema: "accounts",
            table: "users");

        migrationBuilder.AddColumn<string>(
            name: "interface_home",
            schema: "accounts",
            table: "users",
            type: "character varying(8)",
            maxLength: 8,
            nullable: true);

        // Written by hand: an account that kept its three settings gets the default layout, so that
        // it still holds all four once the check below says so.
        migrationBuilder.Sql(
            "UPDATE accounts.users SET interface_home = 'blocks' WHERE interface_language IS NOT NULL;");

        migrationBuilder.AddCheckConstraint(
            name: "ck_users_interface_home",
            schema: "accounts",
            table: "users",
            sql: "interface_home IN ('blocks', 'column')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_users_interface_settings_together",
            schema: "accounts",
            table: "users",
            sql: "(interface_language IS NULL) = (interface_theme IS NULL) AND (interface_theme IS NULL) = (interface_character IS NULL) AND (interface_character IS NULL) = (interface_home IS NULL)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_users_interface_home",
            schema: "accounts",
            table: "users");

        migrationBuilder.DropCheckConstraint(
            name: "ck_users_interface_settings_together",
            schema: "accounts",
            table: "users");

        migrationBuilder.DropColumn(
            name: "interface_home",
            schema: "accounts",
            table: "users");

        migrationBuilder.AddCheckConstraint(
            name: "ck_users_interface_settings_together",
            schema: "accounts",
            table: "users",
            sql: "(interface_language IS NULL) = (interface_theme IS NULL) AND (interface_theme IS NULL) = (interface_character IS NULL)");
    }
}
