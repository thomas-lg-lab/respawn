import type { LibraryGame } from '@respawn/data';

/**
 * What the screens make of the games the browser holds: which ones a search and a filter
 * keep, and in what order. The whole library is in the browser, so none of this asks the API.
 */

/** The filters, in the order of their pills. */
export const FILTERS = ['all', 'launched', 'recent', 'never'] as const;
export type LibraryFilter = (typeof FILTERS)[number];

/** How the library is shown, covers first: the default. */
export const VIEWS = ['covers', 'list'] as const;
export type LibraryView = (typeof VIEWS)[number];

/** The orders, in the order of the list that offers them. */
export const SORTS = ['last_played', 'playtime', 'name'] as const;
export type LibrarySort = (typeof SORTS)[number];

/** A store said it was played: the same count as the home's figures. */
function launched(game: LibraryGame): boolean {
  return (game.playtime_seconds ?? 0) > 0;
}

/**
 * `never` is every game no store says was played, as the home counts them: one whose
 * playtime is unknown is among them, and its row says "unknown", not zero.
 */
const KEEPS: Record<LibraryFilter, (game: LibraryGame) => boolean> = {
  all: () => true,
  launched,
  recent: (game) => (game.recent_playtime_seconds ?? 0) > 0,
  never: (game) => !launched(game),
};

/** Lower case and without accents, so "pokemon" finds "Pokémon". */
function plain(text: string): string {
  return text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase();
}

/**
 * The games whose title holds what was typed; all of them when nothing was. A game no source
 * has named yet has no title to find.
 */
export function matching(games: readonly LibraryGame[], typed: string): LibraryGame[] {
  const needle = plain(typed.trim());
  return needle ? games.filter((game) => plain(game.name ?? '').includes(needle)) : [...games];
}

/** A letter or a figure: what a word is made of. */
const IN_WORD = /[\p{L}\p{N}]/u;
/** A mark that goes with the letter before it, when an accent is written apart. */
const MARK = /\p{M}/u;

/**
 * How many letters a search is made of, as `matching` reads it: without the case, the accents
 * and the signs it ignores. None means the search asks for nothing.
 */
export function letters(typed: string): number {
  return [...plain(typed.trim())].length;
}

/** Whether a title holds what was typed at the start of one of its words. */
function startsWord(title: string, needle: string): boolean {
  for (let at = title.indexOf(needle); at !== -1; at = title.indexOf(needle, at + 1)) {
    if (at === 0 || !IN_WORD.test(title.charAt(at - 1))) return true;
  }
  return false;
}

/**
 * The games a search suggests for what was typed, the closest first: a title that starts with
 * it, then one a word of which does, then any that holds it; among those as close, the most
 * played first. "he" is more likely the start of a title than the end of "The". None when the
 * search asks for nothing: only signs that `matching` ignores.
 */
export function suggested(
  games: readonly LibraryGame[],
  typed: string,
  language: string,
): LibraryGame[] {
  const needle = plain(typed.trim());
  if (!needle) return [];

  const distance = (game: LibraryGame) => {
    const title = plain(game.name ?? '');
    return title.startsWith(needle) ? 0 : startsWord(title, needle) ? 1 : 2;
  };
  // The sort keeps the order of what it cannot tell apart: the playtime's, then the title's.
  return sorted(matching(games, typed), 'playtime', language).sort(
    (a, b) => distance(a) - distance(b),
  );
}

/** The title the Library is searched for, written on its address: the header's search leads there. */
export const LIBRARY_SEARCH = 'q';

/** A title cut around what was typed, to show where it was found. */
export interface FoundTitle {
  readonly before: string;
  readonly found: string;
  readonly after: string;
}

/**
 * Where a title holds what was typed, found the way `matching` finds it: whatever the case
 * and the accents. At the start of a word when it is there, since that is what ranked the
 * title first; else where it first is. `null` when the title does not hold it, or when nothing
 * was typed.
 */
export function foundIn(title: string, typed: string): FoundTitle | null {
  const needle = plain(typed.trim());
  if (!needle) return null;

  let first: FoundTitle | null = null;
  for (let start = 0; start < title.length; start++) {
    // A find never starts on what the search ignores: an accent written apart, a sign.
    if (!plain(title.charAt(start)) || !plain(title.slice(start)).startsWith(needle)) continue;

    let end = start + 1;
    while (plain(title.slice(start, end)).length < needle.length) end++;
    // An accent written apart stays with its letter.
    while (end < title.length && MARK.test(title.charAt(end))) end++;

    const found = {
      before: title.slice(0, start),
      found: title.slice(start, end),
      after: title.slice(end),
    };
    if (start === 0 || !IN_WORD.test(title.charAt(start - 1))) return found;
    first ??= found;
  }
  return first;
}

/** The games a filter keeps. */
export function kept(games: readonly LibraryGame[], filter: LibraryFilter): LibraryGame[] {
  return games.filter(KEEPS[filter]);
}

/** How many games each filter would keep. */
export function filterCounts(games: readonly LibraryGame[]): Record<LibraryFilter, number> {
  const count = (filter: LibraryFilter) => games.filter(KEEPS[filter]).length;
  return {
    all: games.length,
    launched: count('launched'),
    recent: count('recent'),
    never: count('never'),
  };
}

/** The larger first, what is not known last. */
function larger(a: number | null, b: number | null): number {
  if (a === null || b === null) return a === b ? 0 : a === null ? 1 : -1;
  return b - a;
}

function moment(date: string | null): number | null {
  return date === null ? null : Date.parse(date);
}

/**
 * The games in an order: the last session or the playtime, the latest or the longest first
 * and what the store did not say last, or the title as the language sorts it. Two games an
 * order cannot tell apart follow their titles, so a list never changes between two looks.
 */
export function sorted(
  games: readonly LibraryGame[],
  sort: LibrarySort,
  language: string,
): LibraryGame[] {
  const titles = new Intl.Collator(language, { sensitivity: 'base', numeric: true });
  // A game no source has named yet comes after every title.
  const byTitle = (a: LibraryGame, b: LibraryGame) =>
    a.name === null || b.name === null
      ? Number(a.name === null) - Number(b.name === null)
      : titles.compare(a.name, b.name);
  const first: Record<LibrarySort, (a: LibraryGame, b: LibraryGame) => number> = {
    last_played: (a, b) => larger(moment(a.last_played_at), moment(b.last_played_at)),
    playtime: (a, b) => larger(a.playtime_seconds, b.playtime_seconds),
    name: () => 0,
  };
  return [...games].sort((a, b) => first[sort](a, b) || byTitle(a, b));
}

/** No store lists the game any more: every copy of it is flagged. */
export function isMissing(game: LibraryGame): boolean {
  return game.entries.length > 0 && game.entries.every((entry) => entry.missing_since !== null);
}

/**
 * The address that asks the Steam client of this machine to launch the game: `steam://run/`
 * and the game's number at Steam. `null` when no copy of it is still listed there, or when
 * what the store gave as its number is not one: nothing else ever goes into that address.
 */
export function steamLaunchAddress(game: LibraryGame): string | null {
  const owned = game.entries.find(
    (entry) =>
      entry.source === 'steam' && entry.missing_since === null && /^\d+$/.test(entry.external_id),
  );
  return owned ? `steam://run/${owned.external_id}` : null;
}

/**
 * The game's page on the Steam store, from any copy of it Steam knows, listed or not. `null`
 * when Steam knows none, or when what it gave as the game's number is not one.
 */
export function steamStoreAddress(stores: readonly { source: string; external_id: string }[]) {
  const known = stores.find((store) => store.source === 'steam' && /^\d+$/.test(store.external_id));
  return known ? `https://store.steampowered.com/app/${known.external_id}/` : null;
}

/** The longest playtime among the games: what a full bar stands for. */
export function longestPlaytime(games: readonly LibraryGame[]): number {
  return games.reduce((longest, game) => Math.max(longest, game.playtime_seconds ?? 0), 0);
}

/**
 * The hue of a game's placeholder cover, 0 to 359: always the same for one game, and spread
 * over the wheel from one game to the next.
 */
export function coverHue(gameId: string): number {
  let hash = 0;
  for (const letter of gameId) hash = (hash * 31 + letter.charCodeAt(0)) >>> 0;
  return hash % 360;
}
