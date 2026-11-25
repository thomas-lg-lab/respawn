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
 * The items come heaviest first; the ones whose weight is not above zero get no tile.
 * `ratio` is the map's width over its height, so that a tile is cut along its longer side
 * as it is drawn.
 *
 * The cut: the items are split in two where the first part comes closest under half of the
 * sum, the rectangle is split in the same proportion, and each part is cut again. The heavier
 * part goes to the left or the top, unless `flipped` says so of the part's heaviest item: it
 * then goes to the right or the bottom. The tiles keep the order of the items.
 */
export function treemap<T>(
  items: readonly T[],
  weight: (item: T) => number,
  ratio: number,
  flipped: (heaviest: T) => boolean = () => false,
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
    const flip = flipped(head.item);
    if (width * ratio >= height) {
      const [heavy, light] = [width * share, width * (1 - share)];
      cut(part.slice(0, split), flip ? x + light : x, y, heavy, height);
      cut(part.slice(split), flip ? x : x + heavy, y, light, height);
    } else {
      const [heavy, light] = [height * share, height * (1 - share)];
      cut(part.slice(0, split), x, flip ? y + light : y, width, heavy);
      cut(part.slice(split), x, flip ? y : y + heavy, width, light);
    }
  };

  cut(weighed, 0, 0, 100, 100);
  return tiles;
}

/**
 * Which cuts of the map put their heavier part to the right or the bottom: half of them, by a
 * hash of the key of the part's heaviest item. The tiles are then no longer ranked from one
 * corner to the other, while the large ones stay together and the small ones too. The same
 * at every visit: it only changes when the games do, so a map read again does not jump.
 */
export function flippedBy<T>(key: (item: T) => string): (heaviest: T) => boolean {
  return (heaviest) => hash(key(heaviest)) % 2 === 1;
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
