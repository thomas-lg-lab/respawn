import type { HistoryDay, LibraryGame, LibraryHistory, LibraryYears } from '@respawn/data';

/**
 * What the home's blocks make of the library's past: the time played year by year (Steam's
 * Year in Review for the years it covers, the syncs for the others), and the latest days
 * something was played. The history starts at the first sync and is counted reading after
 * reading: a day's time is what its reading found more than the one before, and the day is
 * that reading's, in UTC.
 */

/** How many days the activity shows, the latest first. */
const DAYS = 5;
/** How many games a day names; the others are counted under them. */
const NAMED = 4;

/** A year of the chart. */
export interface PlayedYear {
  readonly year: number;
  readonly seconds: number;
  /** Counted from Steam's Year in Review: the whole year, month by month. */
  readonly reviewed: boolean;
  /** Not a whole year: the one the first sync fell in, the one still running. */
  readonly open: boolean;
  /** The first day counted, as the API writes it: `YYYY-MM-DD`. */
  readonly firstDay: string;
}

/**
 * The years to draw, the earliest first, as the API counts them: from the reviews, then from
 * the syncs. None while nothing was played in any of them: there is nothing to draw.
 */
export function playedYears(told: LibraryYears): PlayedYear[] {
  if (!told.years.some((each) => each.seconds > 0)) return [];
  return told.years.map((each) => ({
    year: each.year,
    seconds: each.seconds,
    reviewed: each.source === 'review',
    open: !each.whole,
    firstDay: each.first_day,
  }));
}

/** A game of a day of the activity. */
export interface PlayedGame {
  readonly id: string;
  /** `null`: the games held do not have it. */
  readonly game: LibraryGame | null;
  /**
   * The library no longer has it. Only said of games that are as fresh as the history: a
   * game the games held do not have yet may just have been added.
   */
  readonly gone: boolean;
  /** What was played of it that day. */
  readonly seconds: number;
}

/** A day of the activity: its games, the most played first, and those it does not name. */
export interface PlayedDay {
  /** The day, as the API writes it: `YYYY-MM-DD`, in UTC. */
  readonly date: string;
  readonly games: readonly PlayedGame[];
  readonly others: number;
}

/**
 * The latest days something was played, the latest first. `settled`: the games are the
 * library as it is now, not an older reading still being asked again; only then is a game
 * they do not have one that left the library.
 */
export function activity(
  history: LibraryHistory,
  games: readonly LibraryGame[],
  settled: boolean,
): PlayedDay[] {
  const byId = new Map(games.map((game) => [game.game_id, game]));
  return history.days
    .slice(-DAYS)
    .reverse()
    .map((day: HistoryDay): PlayedDay => ({
      date: day.date,
      games: day.games.slice(0, NAMED).map((played) => {
        const game = byId.get(played.game_id) ?? null;
        return {
          id: played.game_id,
          game,
          gone: settled && game === null,
          seconds: played.seconds,
        };
      }),
      others: Math.max(0, day.games.length - NAMED),
    }));
}
