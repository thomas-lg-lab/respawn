import type { LibraryGame } from '@respawn/data';
import { kept, sorted } from '@app/games/game-list';
import { type Recency, playedRecency } from '@app/games/time-map';

/**
 * What the Time tab draws: the most played games, each a tile as large as its playtime, and
 * the other launched games together under them, so that the whole of the time is there
 * without the long tail taking most of the drawing.
 */

/** How many games the Time tab names: more than the home's preview, which has less room. */
export const NAMED = 30;

/** One of the most played games, coloured by its last session. */
export interface GameTile {
  readonly kind: 'game';
  readonly id: string;
  readonly game: LibraryGame;
  readonly seconds: number;
  readonly recency: Recency;
}

/** The launched games the map does not name, together under it, not drawn to scale. */
export interface OthersTile {
  readonly kind: 'others';
  readonly id: 'others';
  readonly count: number;
  readonly seconds: number;
}

export type TimeTile = GameTile | OthersTile;

export interface TimeMap {
  /** The named games, the heaviest first: the tiles of the drawing. */
  readonly tiles: readonly GameTile[];
  /** The other launched games, or `null` when every one is named. */
  readonly others: OthersTile | null;
  /** The playtime of every launched game: the whole map. */
  readonly seconds: number;
  readonly launched: number;
}

const playtimeOf = (game: LibraryGame) => game.playtime_seconds ?? 0;

/** The map, or `null` when fewer than two games were launched: one tile alone says nothing. */
export function timeTiles(
  games: readonly LibraryGame[],
  language: string,
  now: number,
): TimeMap | null {
  const launched = sorted(kept(games, 'launched'), 'playtime', language);
  if (launched.length < 2) return null;

  const named = launched.slice(0, NAMED);
  const rest = launched.slice(NAMED);
  const tiles: GameTile[] = named.map((game) => ({
    kind: 'game',
    id: game.game_id,
    game,
    seconds: playtimeOf(game),
    recency: playedRecency(game, now),
  }));
  const restSeconds = rest.reduce((total, game) => total + playtimeOf(game), 0);
  return {
    tiles,
    others: rest.length
      ? { kind: 'others', id: 'others', count: rest.length, seconds: restSeconds }
      : null,
    seconds: named.reduce((total, game) => total + playtimeOf(game), restSeconds),
    launched: launched.length,
  };
}
