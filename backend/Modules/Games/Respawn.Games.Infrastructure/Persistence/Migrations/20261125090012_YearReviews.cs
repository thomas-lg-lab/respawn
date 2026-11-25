using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Games.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261125090012_YearReviews : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "monthly_playtime",
            schema: "games",
            columns: table => new
            {
                linked_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                store_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                month = table.Column<DateOnly>(type: "date", nullable: false),
                playtime_seconds = table.Column<long>(type: "bigint", nullable: false),
                sessions = table.Column<int>(type: "integer", nullable: false),
                snapshot_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_monthly_playtime", x => new { x.linked_account_id, x.month, x.store_entry_id });
                table.CheckConstraint("ck_monthly_playtime_figures", "playtime_seconds >= 0 AND sessions >= 0");
                table.CheckConstraint("ck_monthly_playtime_month", "EXTRACT(DAY FROM month) = 1");
                table.ForeignKey(
                    name: "fk_monthly_playtime_linked_accounts_linked_account_id",
                    column: x => x.linked_account_id,
                    principalSchema: "games",
                    principalTable: "linked_accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_monthly_playtime_raw_snapshots_snapshot_id",
                    column: x => x.snapshot_id,
                    principalSchema: "games",
                    principalTable: "raw_snapshots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_monthly_playtime_store_entries_store_entry_id",
                    column: x => x.store_entry_id,
                    principalSchema: "games",
                    principalTable: "store_entries",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "year_reviews",
            schema: "games",
            columns: table => new
            {
                linked_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                year = table.Column<int>(type: "integer", nullable: false),
                snapshot_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_year_reviews", x => new { x.linked_account_id, x.year });
                table.ForeignKey(
                    name: "fk_year_reviews_linked_accounts_linked_account_id",
                    column: x => x.linked_account_id,
                    principalSchema: "games",
                    principalTable: "linked_accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_year_reviews_raw_snapshots_snapshot_id",
                    column: x => x.snapshot_id,
                    principalSchema: "games",
                    principalTable: "raw_snapshots",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_monthly_playtime_snapshot_id",
            schema: "games",
            table: "monthly_playtime",
            column: "snapshot_id");

        migrationBuilder.CreateIndex(
            name: "ix_monthly_playtime_store_entry_id",
            schema: "games",
            table: "monthly_playtime",
            column: "store_entry_id");

        migrationBuilder.CreateIndex(
            name: "ix_year_reviews_snapshot_id",
            schema: "games",
            table: "year_reviews",
            column: "snapshot_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "monthly_playtime",
            schema: "games");

        migrationBuilder.DropTable(
            name: "year_reviews",
            schema: "games");
    }
}
