import type { LibraryGame } from '@respawn/data';
import { mixed, playedRecency, recency, treemap } from './time-map';

const weight = (value: number) => value;
const area = (tile: { width: number; height: number }) => (tile.width * tile.height) / 100;

describe('the time map', () => {
  it('gives each weight a tile as large as its share', () => {
    const tiles = treemap([50, 25, 15, 10], weight, 2);

    const largestFirst = [...tiles].sort((a, b) => b.item - a.item);
    expect(largestFirst.map((tile) => tile.item)).toEqual([50, 25, 15, 10]);
    expect(largestFirst.map((tile) => Math.round(area(tile)))).toEqual([50, 25, 15, 10]);
  });

  it('fills the map, no tile over another', () => {
    const tiles = treemap([40, 30, 12, 9, 5, 3, 1], weight, 2.6);

    expect(tiles.reduce((sum, tile) => sum + area(tile), 0)).toBeCloseTo(100);
    for (const tile of tiles) {
      expect(tile.x).toBeGreaterThanOrEqual(0);
      expect(tile.y).toBeGreaterThanOrEqual(0);
      expect(tile.x + tile.width).toBeLessThanOrEqual(100.0001);
      expect(tile.y + tile.height).toBeLessThanOrEqual(100.0001);
    }
    const apart = (a: (typeof tiles)[number], b: (typeof tiles)[number]) =>
      a.x + a.width <= b.x + 0.0001 ||
      b.x + b.width <= a.x + 0.0001 ||
      a.y + a.height <= b.y + 0.0001 ||
      b.y + b.height <= a.y + 0.0001;
    for (const a of tiles) for (const b of tiles) if (a !== b) expect(apart(a, b)).toBe(true);
  });

  it('cuts along the longer side as the map is drawn', () => {
    const [wideFirst] = treemap([1, 1], weight, 2);
    expect(wideFirst).toMatchObject({ width: 50, height: 100 });

    const [tallFirst] = treemap([1, 1], weight, 0.5);
    expect(tallFirst).toMatchObject({ width: 100, height: 50 });
  });

  it('gives one weight the whole map, and no tile to nothing', () => {
    expect(treemap([7], weight, 2)).toEqual([{ item: 7, x: 0, y: 0, width: 100, height: 100 }]);
    expect(treemap([], weight, 2)).toEqual([]);
    expect(treemap([3, 0, -1], weight, 2).map((tile) => tile.item)).toEqual([3]);
  });

  it('colours a last session by how long ago it was', () => {
    const now = Date.parse('2026-11-23T12:00:00Z');
    const ago = (days: number) => new Date(now - days * 24 * 3600 * 1000).toISOString();

    expect(recency(ago(0), now)).toBe(1);
    expect(recency(ago(14), now)).toBe(1);
    expect(recency(ago(15), now)).toBe(2);
    expect(recency(ago(90), now)).toBe(2);
    expect(recency(ago(91), now)).toBe(3);
    expect(recency(ago(365), now)).toBe(3);
    expect(recency(ago(366), now)).toBe(4);
  });

  it('counts a game no store dates a session of among the oldest', () => {
    expect(recency(null, Date.now())).toBe(4);
  });

  it('colours a game played these two weeks as the latest, though no store dates its session', () => {
    const game = (values: Partial<LibraryGame>): LibraryGame => ({
      game_id: '00000000-0000-4000-8000-000000000001',
      name: 'Hades',
      playtime_seconds: 7200,
      recent_playtime_seconds: 0,
      last_played_at: null,
      entries: [],
      ...values,
    });
    const now = Date.parse('2026-11-24T12:00:00Z');

    expect(playedRecency(game({ recent_playtime_seconds: 1800 }), now)).toBe(1);
    expect(playedRecency(game({}), now)).toBe(4);
    expect(playedRecency(game({ last_played_at: '2026-06-01T12:00:00Z' }), now)).toBe(3);
  });
});

describe('mixed', () => {
  const ids = Array.from({ length: 13 }, (_, index) => `game-${index}`);

  it('keeps every item, in an order of its own', () => {
    const order = mixed(ids, (id) => id);

    expect([...order].sort()).toEqual([...ids].sort());
    expect(order).not.toEqual(ids);
  });

  it('gives the same order whatever order the items come in', () => {
    expect(mixed([...ids].reverse(), (id) => id)).toEqual(mixed(ids, (id) => id));
  });
});
