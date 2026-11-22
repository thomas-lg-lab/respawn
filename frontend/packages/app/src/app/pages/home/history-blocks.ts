import type { HistoryDay, LibraryGame, LibraryHistory } from '@respawn/data';

/**
 * What the home's blocks make of the history: the time played year by year, and the latest
 * days something was played. The history starts at the first sync and is counted reading
 * after reading: a day's time is what its reading found more than the one before, and the
 * day is that reading's, in UTC.
 */

/** How many days the activity shows, the latest first. */
const DAYS = 5;
/** How many games a day names; the others are counted under them. */
const NAMED = 4;

/** A year of the history. */
export interface PlayedYear {
  readonly year: number;
  readonly seconds: number;
  /**
   * Not a whole year: the one the history starts in, counted from the first sync only, and
   * the one that is still running.
   */
  readonly open: boolean;
}

const yearOf = (day: string) => Number(day.slice(0, 4));

/**
 * The time played each year, from the year of the first sync to the one `now` is in, in
 * UTC as the days are; a year with nothing played in between is there, at zero. None while
 * nothing was played since the first sync: there is nothing to draw.
 */
export function playedYears(history: LibraryHistory, now: number): PlayedYear[] {
  if (history.tracking_since === null || history.days.length === 0) return [];

  const played = new Map<number, number>();
  for (const day of history.days) {
    const seconds = day.games.reduce((sum, game) => sum + game.seconds, 0);
    played.set(yearOf(day.date), (played.get(yearOf(day.date)) ?? 0) + seconds);
  }

  const first = new Date(history.tracking_since).getUTCFullYear();
  // A clock that is behind must not leave out a year the history already has.
  const last = Math.max(new Date(now).getUTCFullYear(), ...played.keys());
  const years: PlayedYear[] = [];
  for (let year = Math.min(first, ...played.keys()); year <= last; year++) {
    years.push({ year, seconds: played.get(year) ?? 0, open: year <= first || year === last });
  }
  return years;
}

/** A game of a day of the activity. */
export interface PlayedGame {
  readonly id: string;
  /** `null`: the library names no such game any more, or no source named it. */
  readonly game: LibraryGame | null;
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

/** The latest days something was played, the latest first. */
export function activity(history: LibraryHistory, games: readonly LibraryGame[]): PlayedDay[] {
  const byId = new Map(games.map((game) => [game.game_id, game]));
  return history.days
    .slice(-DAYS)
    .reverse()
    .map((day: HistoryDay): PlayedDay => ({
      date: day.date,
      games: day.games.slice(0, NAMED).map((played) => ({
        id: played.game_id,
        game: byId.get(played.game_id) ?? null,
        seconds: played.seconds,
      })),
      others: Math.max(0, day.games.length - NAMED),
    }));
}
