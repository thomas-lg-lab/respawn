using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Accounts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Groups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "groups",
                schema: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_groups_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "accounts",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "group_memberships",
                schema: "accounts",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_memberships", x => new { x.group_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_group_memberships_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "accounts",
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_group_memberships_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "accounts",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_group_memberships_user_id",
                schema: "accounts",
                table: "group_memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_groups_is_default",
                schema: "accounts",
                table: "groups",
                column: "is_default",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_groups_owner_id",
                schema: "accounts",
                table: "groups",
                column: "owner_id");

            // Added by hand: the accounts created before groups existed. The default group goes to the
            // first administrator, and every account joins it. Nothing happens on an empty database.
            migrationBuilder.Sql(
                """
                INSERT INTO accounts.groups (id, name, owner_id, is_default, created_at)
                SELECT gen_random_uuid(), 'Everyone', id, TRUE, now()
                FROM accounts.users
                ORDER BY is_admin DESC, created_at, id
                LIMIT 1;

                INSERT INTO accounts.group_memberships (group_id, user_id, joined_at)
                SELECT g.id, u.id, now()
                FROM accounts.groups g CROSS JOIN accounts.users u
                WHERE g.is_default;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "group_memberships",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "groups",
                schema: "accounts");
        }
    }
}
