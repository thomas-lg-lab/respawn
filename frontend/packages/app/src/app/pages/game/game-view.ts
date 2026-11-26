import type { GameMonth, LibraryMonth, LibraryYear } from '@respawn/data';

/**
 * What the page of a game makes of what the API said of it: its calendar, its best month, and
 * what else was played in a month. All of it in UTC, as the API counts months and years.
 */

/** A month as `YYYY-MM`. */
export type MonthKey = string;

/** The month a `YYYY-MM-DD` day is in. */
export function monthOf(day: string): MonthKey {
  return day.slice(0, 7);
}

/** The month a moment is in, in UTC. */
export function monthAt(now: number): MonthKey {
  const date = new Date(now);
  return `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, '0')}`;
}

/**
 * What a cell of the calendar is: a month the game was `played` in, one that was counted and
 * had `none`, one nothing counted (`uncounted`: before the first reading in a year with no
 * review), or one still to come (`later`).
 */
export type CellKind = 'played' | 'none' | 'uncounted' | 'later';

/** How dark a played month is drawn, 1 to 4: its share of the game's best month. */
export type Shade = 1 | 2 | 3 | 4;

export interface CalendarCell {
  readonly month: MonthKey;
  readonly kind: CellKind;
  readonly seconds: number;
  /** `null` for a month not played. */
  readonly shade: Shade | null;
}

/** A year the game was played in: its twelve months and its time. */
export interface CalendarYear {
  readonly kind: 'year';
  readonly year: number;
  readonly seconds: number;
  readonly cells: readonly CalendarCell[];
}

/**
 * Years in a row with no time of the game, folded into one line. `noSession` between two years
 * played, `nothingSince` at the end, `notCounted` when nothing was counted in any of them.
 */
export interface CalendarGap {
  readonly kind: 'gap';
  readonly from: number;
  readonly to: number;
  readonly said: 'noSession' | 'nothingSince' | 'notCounted';
}

export type CalendarRow = CalendarYear | CalendarGap;

export interface Calendar {
  /** From the first year played to the current one; none when no month was played. */
  readonly rows: readonly CalendarRow[];
  /** The month played the most, the earliest of the best ones. */
  readonly best: CalendarCell | null;
  /** The last month played. */
  readonly last: MonthKey | null;
  /**
   * The time of the game no month holds: played before the months were counted, or in months
   * nothing counted. Zero under an hour, which the months may just round away.
   */
  readonly unplaced: number;
}

const HOUR = 3600;

/**
 * The calendar of a game: a row of twelve months per year played, from the first to the
 * current one; years with none folded. `years` are the library's, which say what days were
 * counted at all; `total` is the game's playtime, `now` the moment the current month is taken
 * from.
 */
export function calendar(
  months: readonly GameMonth[],
  years: readonly LibraryYear[],
  total: number | null,
  now: number,
): Calendar {
  const played = new Map<MonthKey, number>();
  for (const month of months) {
    if (month.seconds <= 0) continue;
    const key = monthOf(month.month);
    played.set(key, (played.get(key) ?? 0) + month.seconds);
  }
  const keys = [...played.keys()].sort();
  const earliest = keys[0];
  const latest = keys.at(-1);
  const placed = [...played.values()].reduce((sum, seconds) => sum + seconds, 0);
  const unplaced = total === null || total - placed < HOUR ? 0 : total - placed;
  if (!earliest || !latest) return { rows: [], best: null, last: null, unplaced };

  const best = Math.max(...played.values());
  const current = monthAt(now);
  // A month was counted when one of the library's years covers any of its days.
  const counted = (key: MonthKey) =>
    years.some((year) => `${key}-01` <= year.last_day && `${key}-31` >= year.first_day);

  const cell = (key: MonthKey): CalendarCell => {
    const seconds = played.get(key) ?? 0;
    if (seconds > 0) {
      const step = Math.min(3, Math.floor((seconds / best) * 3.999));
      return { month: key, kind: 'played', seconds, shade: (step + 1) as Shade };
    }
    const kind: CellKind = key > current ? 'later' : counted(key) ? 'none' : 'uncounted';
    return { month: key, kind, seconds: 0, shade: null };
  };

  const first = Number(earliest.slice(0, 4));
  const last = Number(current.slice(0, 4));
  const rows: CalendarRow[] = [];
  let folded: CalendarYear[] = [];
  const fold = (trailing: boolean) => {
    const from = folded[0];
    const to = folded.at(-1);
    if (!from || !to) return;
    const known = folded.flatMap((year) => year.cells).filter((each) => each.kind !== 'later');
    rows.push({
      kind: 'gap',
      from: from.year,
      to: to.year,
      said: known.every((each) => each.kind === 'uncounted')
        ? 'notCounted'
        : trailing
          ? 'nothingSince'
          : 'noSession',
    });
    folded = [];
  };
  for (let year = first; year <= last; year++) {
    const cells = Array.from({ length: 12 }, (_, index) =>
      cell(`${year}-${String(index + 1).padStart(2, '0')}`),
    );
    const seconds = cells.reduce((sum, each) => sum + each.seconds, 0);
    const row: CalendarYear = { kind: 'year', year, seconds, cells };
    if (seconds === 0) {
      folded.push(row);
      continue;
    }
    fold(false);
    rows.push(row);
  }
  fold(true);

  const cells = rows.flatMap((row) => (row.kind === 'year' ? row.cells : []));
  return {
    rows,
    best: cells.find((each) => each.seconds === best) ?? null,
    last: latest,
    unplaced,
  };
}

/** What the game was played in a year, from its months. */
export function secondsIn(months: readonly GameMonth[], year: number): number {
  const prefix = `${year}-`;
  return months
    .filter((month) => month.month.startsWith(prefix))
    .reduce((sum, month) => sum + month.seconds, 0);
}

/** A game of the month as its list shows it. */
export interface MonthRow {
  readonly id: string;
  readonly name: string | null;
  readonly seconds: number;
  /** The game the page is about. */
  readonly own: boolean;
}

/** What was played in a month, from the page's game. */
export interface MonthDetail {
  /** The time of the page's game that month. */
  readonly seconds: number;
  /** Its place among the games of the month, the most played first. */
  readonly rank: number;
  /** How many games were played that month. */
  readonly count: number;
  /** The time of every game that month. */
  readonly total: number;
  /** The most played games, the page's own among them. */
  readonly rows: readonly MonthRow[];
  /** The ones not listed, together. */
  readonly others: { readonly count: number; readonly seconds: number } | null;
  /** The longest time of the list: what a full bar stands for. */
  readonly scale: number;
}

/** How many games of a month are listed by name. */
const LISTED = 4;

/**
 * A month from the page's game: the games played then, the most played first, the page's own
 * always among the listed ones. `ownSeconds` is its time from its own months, for a month whose
 * answer does not name it (a month the library counts day by day may hold other days).
 */
export function monthDetail(month: LibraryMonth, gameId: string, ownSeconds: number): MonthDetail {
  const games: MonthRow[] = month.games
    .filter((game) => game.seconds > 0)
    .map((game) => ({
      id: game.game_id,
      name: game.name,
      seconds: game.seconds,
      own: game.game_id === gameId,
    }));
  let own = games.find((game) => game.own);
  if (!own && ownSeconds > 0) {
    own = { id: gameId, name: null, seconds: ownSeconds, own: true };
    games.push(own);
  }
  // The order of the answer is kept between equals: the most played first.
  games.sort((a, b) => b.seconds - a.seconds);

  const rank = own ? games.indexOf(own) + 1 : 0;
  const rows = games.slice(0, LISTED);
  if (own && rank > LISTED) rows[LISTED - 1] = own;
  const rest = games.filter((game) => !rows.includes(game));
  return {
    seconds: own?.seconds ?? 0,
    rank,
    count: games.length,
    total: games.reduce((sum, game) => sum + game.seconds, 0),
    rows,
    others: rest.length
      ? { count: rest.length, seconds: rest.reduce((sum, game) => sum + game.seconds, 0) }
      : null,
    scale: games[0]?.seconds ?? 0,
  };
}
