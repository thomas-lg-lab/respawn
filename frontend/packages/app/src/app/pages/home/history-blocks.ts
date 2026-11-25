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

/** A year of the chart: a whole one. */
export interface PlayedYear {
  readonly year: number;
  readonly seconds: number;
  /** Counted from Steam's Year in Review, month by month; otherwise from the syncs. */
  readonly reviewed: boolean;
}

/** What the chart draws, and the years it leaves out. */
export interface PlayedYears {
  /** The whole years, the earliest first. None while no whole year has time. */
  readonly years: readonly PlayedYear[];
  /**
   * Every year counted only in part, the earliest first: the one still running, the one just
   * over while its review is awaited, one a linked account has no review of. Left out of the
   * chart, where a part of it would read as the whole.
   */
  readonly pending: readonly number[];
}

/**
 * The years the chart draws, as the API counts them: only the whole ones, from the reviews or,
 * should a store's review ever be missing, from a year the syncs covered entirely. A year
 * counted only from the first sync, or up to today, is not drawn, but named. Nothing at all
 * while nothing was played in any year.
 */
export function playedYears(told: LibraryYears): PlayedYears {
  if (!told.years.some((each) => each.seconds > 0)) return { years: [], pending: [] };
  const whole = told.years.filter((each) => each.whole);
  return {
    years: whole.some((each) => each.seconds > 0)
      ? whole.map((each) => ({
          year: each.year,
          seconds: each.seconds,
          reviewed: each.source === 'review',
        }))
      : [],
    pending: told.years.filter((each) => !each.whole).map((each) => each.year),
  };
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
