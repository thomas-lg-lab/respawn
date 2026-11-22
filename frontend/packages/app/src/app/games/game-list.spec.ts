import type { LibraryEntry, LibraryGame } from '@respawn/data';
import {
  coverHue,
  filterCounts,
  isMissing,
  kept,
  longestPlaytime,
  matching,
  sorted,
  steamLaunchAddress,
} from '@app/games/game-list';

function game(name: string, values: Partial<LibraryGame> = {}): LibraryGame {
  return {
    game_id: `id-${name}`,
    name,
    playtime_seconds: 0,
    recent_playtime_seconds: 0,
    last_played_at: null,
    entries: [],
    ...values,
  };
}

function entry(missingSince: string | null): LibraryEntry {
  return {
    store_entry_id: 'e',
    linked_account_id: 'a',
    source: 'steam',
    external_id: '620',
    playtime_seconds: 0,
    recent_playtime_seconds: 0,
    last_played_at: null,
    first_seen_at: '2026-11-18T10:05:00Z',
    as_of: '2026-11-20T04:02:05Z',
    missing_since: missingSince,
  };
}

const names = (games: LibraryGame[]) => games.map((each) => each.name);

const PORTAL = game('Portal 2', {
  playtime_seconds: 7200,
  recent_playtime_seconds: 3600,
  last_played_at: '2026-11-20T20:00:00Z',
});
const HADES = game('Hades', { playtime_seconds: 360000, last_played_at: '2025-03-02T20:00:00Z' });
const POKEMON = game('Pokémon', { playtime_seconds: null, recent_playtime_seconds: null });
const CELESTE = game('celeste');
const ALL = [PORTAL, HADES, POKEMON, CELESTE];

describe('library view', () => {
  it('finds a title whatever its case and its accents, and everything when nothing is typed', () => {
    expect(names(matching(ALL, 'pokemon'))).toEqual(['Pokémon']);
    expect(names(matching(ALL, '  PORT '))).toEqual(['Portal 2']);
    expect(matching(ALL, '   ')).toHaveLength(4);
    expect(matching(ALL, 'zelda')).toEqual([]);
  });

  it('keeps what a filter asks for', () => {
    expect(names(kept(ALL, 'launched'))).toEqual(['Portal 2', 'Hades']);
    expect(names(kept(ALL, 'recent'))).toEqual(['Portal 2']);
    expect(kept(ALL, 'all')).toHaveLength(4);
  });

  it('counts an unknown playtime among the never launched, as the home does', () => {
    expect(names(kept(ALL, 'never'))).toEqual(['Pokémon', 'celeste']);
    expect(filterCounts(ALL)).toEqual({ all: 4, launched: 2, recent: 1, never: 2 });
  });

  it('sorts by title as the language does', () => {
    expect(names(sorted(ALL, 'name', 'en'))).toEqual(['celeste', 'Hades', 'Pokémon', 'Portal 2']);
  });

  it('puts a game no source has named after every title, and finds it by no search', () => {
    const unnamed = game('?', { name: null });

    expect(sorted([unnamed, ...ALL], 'name', 'en').at(-1)).toBe(unnamed);
    expect(matching([unnamed, ...ALL], '?')).toEqual([]);
    expect(matching([unnamed, ...ALL], '')).toHaveLength(5);
  });

  it('puts the latest session first, and the games with none last, by title', () => {
    expect(names(sorted(ALL, 'last_played', 'en'))).toEqual([
      'Portal 2',
      'Hades',
      'celeste',
      'Pokémon',
    ]);
  });

  it('puts the longest playtime first, and an unknown one after a zero', () => {
    expect(names(sorted(ALL, 'playtime', 'en'))).toEqual([
      'Hades',
      'Portal 2',
      'celeste',
      'Pokémon',
    ]);
  });

  it('leaves the games it was given in their order', () => {
    sorted(ALL, 'name', 'en');

    expect(names(ALL)).toEqual(['Portal 2', 'Hades', 'Pokémon', 'celeste']);
  });

  it('flags a game once no store lists any copy of it', () => {
    expect(isMissing(game('Gone', { entries: [entry('2026-11-19T04:00:00Z')] }))).toBe(true);
    expect(isMissing(game('Half', { entries: [entry('2026-11-19T04:00:00Z'), entry(null)] }))).toBe(
      false,
    );
    expect(isMissing(game('Bare'))).toBe(false);
  });

  it('scales the bars on the most played game', () => {
    expect(longestPlaytime(ALL)).toBe(360000);
    expect(longestPlaytime([])).toBe(0);
  });

  it('gives a game the same hue each time, on the wheel', () => {
    const hue = coverHue('0b0f6f3e-6a59-4c8c-9a55-2d7f3f0d6c11');

    expect(hue).toBe(coverHue('0b0f6f3e-6a59-4c8c-9a55-2d7f3f0d6c11'));
    expect(hue).toBeGreaterThanOrEqual(0);
    expect(hue).toBeLessThan(360);
    expect(Number.isInteger(hue)).toBe(true);
  });

  it('gives the address that launches a game in Steam, from its number there', () => {
    expect(steamLaunchAddress(game('Portal 2', { entries: [entry(null)] }))).toBe(
      'steam://run/620',
    );
  });

  it('gives no address to launch a game Steam no longer lists, or one from another store', () => {
    const gone = game('Gone', { entries: [entry('2026-11-19T04:00:00Z')] });
    const elsewhere = game('Elsewhere', { entries: [{ ...entry(null), source: 'gog' }] });

    expect(steamLaunchAddress(gone)).toBeNull();
    expect(steamLaunchAddress(elsewhere)).toBeNull();
    expect(steamLaunchAddress(game('Unowned'))).toBeNull();
  });

  it('never writes into the address a number that is not one', () => {
    const odd = game('Odd', { entries: [{ ...entry(null), external_id: '620/../x' }] });

    expect(steamLaunchAddress(odd)).toBeNull();
  });
});
