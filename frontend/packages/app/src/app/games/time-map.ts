import type { LibraryGame } from '@respawn/data';

/**
 * The time map: a rectangle cut into tiles, a tile's area being its share of the whole, and
 * its colour how long ago its game was played.
 */

/** How many games the home's preview of the map names: past that, a tile says nothing there. */
export const MAPPED = 13;

/** What a tile stands for, and its place in the map, in percent of its width and height. */
export interface Tile<T> {
  readonly item: T;
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

/**
 * Cuts the map into one tile per item, each as large as its weight's share of their sum.
 * The items are placed in the order they come (`mixed` scatters them); the ones whose weight
 * is not above zero get no tile.
 * `ratio` is the map's width over its height, so that a tile is cut along its longer side
 * as it is drawn.
 *
 * The cut: the items are split in two where the first part comes closest under half of the
 * sum, the rectangle is split in the same proportion, and each part is cut again.
 */
export function treemap<T>(
  items: readonly T[],
  weight: (item: T) => number,
  ratio: number,
): Tile<T>[] {
  const tiles: Tile<T>[] = [];
  const weighed = items
    .map((item) => ({ item, weight: weight(item) }))
    .filter((each) => each.weight > 0);

  const cut = (part: typeof weighed, x: number, y: number, width: number, height: number) => {
    const [head, ...rest] = part;
    if (!head) return;
    if (!rest.length) {
      tiles.push({ item: head.item, x, y, width, height });
      return;
    }
    const sum = part.reduce((total, each) => total + each.weight, 0);
    // The first part always holds the heaviest, and never everything.
    let first = head.weight;
    let split = 1;
    for (const next of rest.slice(0, -1)) {
      if (first + next.weight > sum / 2) break;
      first += next.weight;
      split++;
    }
    const share = first / sum;
    if (width * ratio >= height) {
      cut(part.slice(0, split), x, y, width * share, height);
      cut(part.slice(split), x + width * share, y, width * (1 - share), height);
    } else {
      cut(part.slice(0, split), x, y, width, height * share);
      cut(part.slice(split), x, y + height * share, width, height * (1 - share));
    }
  };

  cut(weighed, 0, 0, 100, 100);
  return tiles;
}

/**
 * The items in an order that looks drawn at random and is the same at every visit: by a hash of
 * each one's key. Given to the cut, it scatters the tiles over the map, the large and the small,
 * the cold and the warm, rather than ranking them from one corner to the other. The order only
 * changes when the items do, so a map read again does not jump.
 */
export function mixed<T>(items: readonly T[], key: (item: T) => string): T[] {
  return items
    .map((item) => ({ item, rank: hash(key(item)) }))
    .sort((a, b) => a.rank - b.rank)
    .map((each) => each.item);
}

/** FNV-1a on 32 bits: enough to spread a few dozen ids, and the same in every browser. */
function hash(text: string): number {
  let value = 0x811c9dc5;
  for (let index = 0; index < text.length; index++) {
    value ^= text.charCodeAt(index);
    value = Math.imul(value, 0x01000193);
  }
  return value >>> 0;
}

/** How long ago a game was played, as the map colours it: 1 the latest, 4 the oldest. */
export type Recency = 1 | 2 | 3 | 4;

const DAY = 24 * 60 * 60 * 1000;
/** The last day of each step but the oldest: two weeks, three months, a year. */
const STEPS = [14, 90, 365];

/**
 * The step of a last session seen from `now`. A game no store dates a session of is among
 * the oldest: it was played, and nothing says it was lately.
 */
export function recency(lastPlayedAt: string | null, now: number): Recency {
  if (lastPlayedAt === null) return 4;
  const days = (now - Date.parse(lastPlayedAt)) / DAY;
  const step = STEPS.findIndex((last) => days <= last);
  return (step === -1 ? 4 : step + 1) as Recency;
}

/**
 * The step a game's tile is coloured with. A game played these two weeks whose last session
 * no store dates is among the latest: the two weeks say more than the missing date.
 */
export function playedRecency(game: LibraryGame, now: number): Recency {
  return game.last_played_at === null && (game.recent_playtime_seconds ?? 0) > 0
    ? 1
    : recency(game.last_played_at, now);
}
