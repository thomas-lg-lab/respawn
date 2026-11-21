using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Respawn.Games.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KnownZeros : Migration
    {
        /// <summary>
        /// The observations recorded before a zero that says nothing stopped being kept as a known total:
        /// their zero becomes "not known", as it is now recorded. Two cases, the two of the rule. A
        /// reading that gave no time to any game was a store hiding the playtime. A zero recorded when a
        /// total was already known was a poor answer arriving late: the readings recorded before it for
        /// the same linked account and store entry tell, since a reading in its order carried the total
        /// over and never wrote a zero there. A zero beside games that have a total, with nothing known
        /// before, is a game never launched, and stays. Running it again changes nothing.
        /// </summary>
        internal const string Sql =
            """
            UPDATE games.observations o
            SET known_playtime_seconds = NULL
            FROM games.raw_snapshots s
            WHERE s.id = o.snapshot_id
              AND o.known_playtime_seconds = 0
              AND (
                  NOT EXISTS (
                      SELECT 1 FROM games.observations beside
                      WHERE beside.snapshot_id = o.snapshot_id AND beside.playtime_seconds > 0)
                  OR EXISTS (
                      SELECT 1
                      FROM games.observations earlier
                      JOIN games.raw_snapshots recorded ON recorded.id = earlier.snapshot_id
                      WHERE earlier.store_entry_id = o.store_entry_id
                        AND recorded.linked_account_id = s.linked_account_id
                        AND earlier.known_playtime_seconds > 0
                        AND (recorded.recorded_at, recorded.id) < (s.recorded_at, s.id)))
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to bring back: which of the unknown totals were zeros is not kept.
        }
    }
}
