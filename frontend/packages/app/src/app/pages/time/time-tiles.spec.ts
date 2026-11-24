import type { LibraryGame } from '@respawn/data';
import { timeTiles } from './time-tiles';

let made = 0;

function game(name: string, playtime: number, values: Partial<LibraryGame> = {}): LibraryGame {
  return {
    game_id: `00000000-0000-4000-8000-${String(++made).padStart(12, '0')}`,
    name,
    playtime_seconds: playtime,
    recent_playtime_seconds: 0,
    last_played_at: null,
    entries: [],
    ...values,
  };
}

const NOW = Date.parse('2026-11-24T12:00:00Z');

describe('the tiles of the Time tab', () => {
  it('names the most played games and puts the other launched ones in one more tile', () => {
    const games = [
      ...Array.from({ length: 15 }, (_, rank) => game(`Game ${rank + 1}`, (20 - rank) * 3600)),
      game('Never launched', 0),
    ];

    const map = timeTiles(games, 'en', NOW)!;

    expect(map.tiles).toHaveLength(14);
    expect(map.tiles[0]).toMatchObject({ kind: 'game', seconds: 20 * 3600 });
    expect(map.tiles.at(-1)).toEqual({
      kind: 'others',
      id: 'others',
      count: 2,
      seconds: (7 + 6) * 3600,
    });
    expect(map.launched).toBe(15);
    expect(map.seconds).toBe(map.tiles.reduce((total, tile) => total + tile.seconds, 0));
  });

  it('needs no tile for the others when every launched game is named', () => {
    const map = timeTiles([game('Hades', 7200), game('Celeste', 3600)], 'en', NOW)!;

    expect(map.tiles.map((tile) => tile.kind)).toEqual(['game', 'game']);
  });

  it('colours each game by its last session', () => {
    const map = timeTiles(
      [
        game('Hades', 7200, { last_played_at: '2026-11-20T12:00:00Z' }),
        game('Celeste', 3600, { last_played_at: '2024-01-01T12:00:00Z' }),
      ],
      'en',
      NOW,
    )!;

    expect(map.tiles.map((tile) => tile.kind === 'game' && tile.recency)).toEqual([1, 4]);
  });

  it('draws nothing under two launched games', () => {
    expect(timeTiles([game('Hades', 7200), game('Celeste', 0)], 'en', NOW)).toBeNull();
    expect(timeTiles([], 'en', NOW)).toBeNull();
  });
});
