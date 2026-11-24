import type { LibraryGame } from '@respawn/data';
import { isMissing, kept, sorted } from '@app/games/game-list';
import { MAPPED, type Recency, playedRecency } from '@app/games/time-map';

/**
 * What the home's blocks make of the games the browser holds: the one to resume, the ones
 * played lately, where the time went, its map and a draw among the never launched. None of it asks
 * the API, and each says only what a store gave: totals, the last two weeks, the last session.
 */

/** How many games "Played recently" shows beside the one to resume. */
const RECENT = 4;
/** How many games the ranking names; the others are summed up under it. */
const RANKED = 6;
/** How many never launched games a draw gives. */
export const PICKS = 3;

/**
 * The game to resume: the one whose last session is the latest, if a store told any. Never
 * one no store lists any more: it left the library, there is nothing to resume.
 */
export function lastPlayed(games: readonly LibraryGame[], language: string): LibraryGame | null {
  const played = games.filter((game) => game.last_played_at !== null && !isMissing(game));
  return sorted(played, 'last_played', language)[0] ?? null;
}

/**
 * The games played lately, beside the one to resume, among those a store still lists.
 * `recent`: those of the last two weeks, the most played first; none when the one to resume
 * is the only one of them. When the store names no game of the two weeks at all (it says
 * nothing of them on some profiles), `last`: the latest sessions.
 */
export interface PlayedRecently {
  readonly by: 'recent' | 'last';
  readonly games: readonly LibraryGame[];
}

export function playedRecently(
  games: readonly LibraryGame[],
  resumed: LibraryGame | null,
  language: string,
): PlayedRecently {
  const listed = games.filter((game) => !isMissing(game));
  const others = listed.filter((game) => game !== resumed);
  // The store told the two weeks as soon as it names one game of them, the resumed one too.
  if (kept(listed, 'recent').length) {
    // By title first: the sort keeps that order between two games played as much.
    const lately = sorted(kept(others, 'recent'), 'name', language).sort(
      (a, b) => (b.recent_playtime_seconds ?? 0) - (a.recent_playtime_seconds ?? 0),
    );
    return { by: 'recent', games: lately.slice(0, RECENT) };
  }

  const played = others.filter((game) => game.last_played_at !== null);
  return { by: 'last', games: sorted(played, 'last_played', language).slice(0, RECENT) };
}

/** Where the time went: the most played games, and what the other launched ones add up to. */
export interface Ranking {
  readonly top: readonly LibraryGame[];
  /** The playtime of every launched game. */
  readonly seconds: number;
  /** The launched games the ranking does not name. */
  readonly others: { readonly count: number; readonly seconds: number };
}

const played = (games: readonly LibraryGame[]) =>
  games.reduce((seconds, game) => seconds + (game.playtime_seconds ?? 0), 0);

export function ranking(games: readonly LibraryGame[], language: string): Ranking {
  const launched = sorted(kept(games, 'launched'), 'playtime', language);
  const top = launched.slice(0, RANKED);
  const seconds = played(launched);
  return {
    top,
    seconds,
    others: { count: launched.length - top.length, seconds: seconds - played(top) },
  };
}

/** A game on the time map preview: what its tile is sized by, and its colour. */
export interface MapGame {
  readonly id: string;
  readonly seconds: number;
  readonly recency: Recency;
}

/**
 * The time map preview: the most played games, the most played first, each with its playtime
 * and how long before `now` it was last played. None when fewer than two games were launched:
 * one tile alone would say nothing.
 */
export function timeMap(games: readonly LibraryGame[], language: string, now: number): MapGame[] {
  const mapped = sorted(kept(games, 'launched'), 'playtime', language).slice(0, MAPPED);
  if (mapped.length < 2) return [];
  return mapped.map((game) => ({
    id: game.game_id,
    seconds: game.playtime_seconds ?? 0,
    recency: playedRecency(game, now),
  }));
}

/**
 * The games a draw is made among: never launched, as the Library's filter counts them, and
 * still listed by a store, since one that is not cannot be launched.
 */
export function neverLaunched(games: readonly LibraryGame[]): LibraryGame[] {
  return kept(games, 'never').filter((game) => !isMissing(game));
}

/** `count` of these, at random, each at most once; all of them when there are no more. */
export function drawn<T>(pool: readonly T[], count: number, random = Math.random): T[] {
  const left = [...pool];
  const picked: T[] = [];
  while (picked.length < count && left.length) {
    picked.push(...left.splice(Math.floor(random() * left.length), 1));
  }
  return picked;
}

/**
 * The draw to show once the games were read again: the one shown before, each game as it now
 * is, while all of them can still be drawn and none is lacking; a new one otherwise. A new
 * reading of the library must not change the draw under the visitor's eyes.
 */
export function keptDraw(
  pool: readonly LibraryGame[],
  shown: readonly LibraryGame[] | undefined,
  count: number,
  random = Math.random,
): LibraryGame[] {
  const byId = new Map(pool.map((game) => [game.game_id, game]));
  const still = (shown ?? []).flatMap((game) => byId.get(game.game_id) ?? []);
  const whole = shown !== undefined && still.length === shown.length;
  return whole && still.length === Math.min(count, pool.length)
    ? still
    : drawn(pool, count, random);
}
