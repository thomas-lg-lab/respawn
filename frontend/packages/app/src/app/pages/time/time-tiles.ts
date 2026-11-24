import type { LibraryGame } from '@respawn/data';
import { kept, sorted } from '@app/games/game-list';
import { MAPPED, type Recency, playedRecency } from '@app/games/time-map';

/**
 * What the Time tab draws: the most played games, each a tile as large as its playtime, and
 * the other launched games together in one more, so that the map is the whole of the time.
 */

/** One of the most played games, coloured by its last session. */
export interface GameTile {
  readonly kind: 'game';
  readonly id: string;
  readonly game: LibraryGame;
  readonly seconds: number;
  readonly recency: Recency;
}

/** The launched games the map does not name, as one tile. */
export interface OthersTile {
  readonly kind: 'others';
  readonly id: 'others';
  readonly count: number;
  readonly seconds: number;
}

export type TimeTile = GameTile | OthersTile;

export interface TimeMap {
  /** The heaviest first, the other games last whatever their weight. */
  readonly tiles: readonly TimeTile[];
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

  const named = launched.slice(0, MAPPED);
  const rest = launched.slice(MAPPED);
  const tiles: TimeTile[] = named.map((game) => ({
    kind: 'game',
    id: game.game_id,
    game,
    seconds: playtimeOf(game),
    recency: playedRecency(game, now),
  }));
  const restSeconds = rest.reduce((total, game) => total + playtimeOf(game), 0);
  if (rest.length) {
    tiles.push({ kind: 'others', id: 'others', count: rest.length, seconds: restSeconds });
  }
  return {
    tiles,
    seconds: named.reduce((total, game) => total + playtimeOf(game), restSeconds),
    launched: launched.length,
  };
}
