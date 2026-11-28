import type { LibraryGame } from '@respawn/data';
import { entry, game } from '@app/testing/games';
import {
  coverHue,
  filterCounts,
  isMissing,
  kept,
  longestPlaytime,
  foundIn,
  letters,
  matching,
  suggested,
  sorted,
  steamLaunchAddress,
  askedLengths,
  askedTags,
  lengthCounts,
  lengthOf,
  shortTag,
  tagCounts,
  withLengths,
  withTags,
} from '@app/games/game-list';

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

  it('counts the letters a search is read with', () => {
    expect(letters('  Poké ')).toBe(4);
    expect(letters('^^')).toBe(0);
    expect(letters('   ')).toBe(0);
    // An accent written apart is not a letter more.
    expect(letters('e\u0301')).toBe(1);
  });

  it('cuts a title at the start of a word when what was typed is there, and leaves a sign out', () => {
    expect(foundIn('Smooth Theory', 'th')).toEqual({
      before: 'Smooth ',
      found: 'Th',
      after: 'eory',
    });
    expect(foundIn('WALL·E', 'wall')).toEqual({ before: '', found: 'WALL', after: '·E' });
    expect(foundIn('WALL·E', 'e')).toEqual({ before: 'WALL·', found: 'E', after: '' });
  });

  it('suggests nothing for a search made of signs alone', () => {
    expect(suggested(ALL, '^^', 'en')).toEqual([]);
    expect(suggested(ALL, '  ', 'en')).toEqual([]);
  });

  it('cuts a title around what was typed, whatever the case and the accents', () => {
    expect(foundIn('Slay the Spire', 'THE')).toEqual({
      before: 'Slay ',
      found: 'the',
      after: ' Spire',
    });
    expect(foundIn('Pokémon', 'poke')).toEqual({ before: '', found: 'Poké', after: 'mon' });
    // An accent written apart stays with its letter.
    expect(foundIn('Poke\u0301mon', 'poke')).toEqual({
      before: '',
      found: 'Poke\u0301',
      after: 'mon',
    });
    expect(foundIn('Pokémon', 'émo')).toEqual({ before: 'Pok', found: 'émo', after: 'n' });
    expect(foundIn('Hades', 'zelda')).toBeNull();
    expect(foundIn('Hades', '  ')).toBeNull();
  });

  it('suggests the titles that start with what was typed, then a word that does, then the rest', () => {
    const games = [
      game('The Witness', { playtime_seconds: 900000 }),
      game('Shadow of the Tomb Raider', { playtime_seconds: 600000 }),
      game('Into the Breach', { playtime_seconds: 300000 }),
      game('Thief', { playtime_seconds: 60 }),
      game('Thumper'),
    ];

    // "th" starts two titles and a word of the others; the most played first among each.
    expect(names(suggested(games, 'th', 'en'))).toEqual([
      'The Witness',
      'Thief',
      'Thumper',
      'Shadow of the Tomb Raider',
      'Into the Breach',
    ]);
    // "he" starts nothing: it is only held, and the playtime alone orders.
    expect(names(suggested(games, 'he', 'en'))).toEqual([
      'The Witness',
      'Shadow of the Tomb Raider',
      'Into the Breach',
    ]);
    expect(names(suggested(games, 'to', 'en'))).toEqual([
      'Shadow of the Tomb Raider',
      'Into the Breach',
    ]);
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
    expect(
      isMissing(game('Gone', { entries: [entry({ missing_since: '2026-11-19T04:00:00Z' })] })),
    ).toBe(true);
    expect(
      isMissing(
        game('Half', { entries: [entry({ missing_since: '2026-11-19T04:00:00Z' }), entry()] }),
      ),
    ).toBe(false);
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
    expect(steamLaunchAddress(game('Portal 2', { entries: [entry()] }))).toBe('steam://run/620');
  });

  it('gives no address to launch a game Steam no longer lists, or one from another store', () => {
    const gone = game('Gone', { entries: [entry({ missing_since: '2026-11-19T04:00:00Z' })] });
    const elsewhere = game('Elsewhere', { entries: [{ ...entry(), source: 'gog' }] });

    expect(steamLaunchAddress(gone)).toBeNull();
    expect(steamLaunchAddress(elsewhere)).toBeNull();
    expect(steamLaunchAddress(game('Unowned'))).toBeNull();
  });

  it('never writes into the address a number that is not one', () => {
    const odd = game('Odd', { entries: [{ ...entry(), external_id: '620/../x' }] });

    expect(steamLaunchAddress(odd)).toBeNull();
  });
});

describe('the genres, themes and lengths', () => {
  const HOUR = 3600;
  const celeste = game('Celeste', { genres: ['Platform', 'Indie'], normally_seconds: 8 * HOUR });
  const hades = game('Hades', {
    genres: ['Role-playing (RPG)', 'Indie'],
    themes: ['Action'],
    normally_seconds: 30 * HOUR,
  });
  const disco = game('Disco Elysium', { genres: ['Role-playing (RPG)'], normally_seconds: 0 });
  const all = [celeste, hades, disco];

  it('puts a game in its length, 30 hours still in the middle one, nothing said as unknown', () => {
    expect(lengthOf({ normally_seconds: 10 * HOUR - 1 })).toBe('short');
    expect(lengthOf({ normally_seconds: 10 * HOUR })).toBe('medium');
    expect(lengthOf({ normally_seconds: 30 * HOUR })).toBe('medium');
    expect(lengthOf({ normally_seconds: 30 * HOUR + 1 })).toBe('long');
    expect(lengthOf({ normally_seconds: 0 })).toBe('unknown');
    expect(lengthOf({ normally_seconds: null })).toBe('unknown');
    // Past 200 hours, a game that never ends: no duration to count.
    expect(lengthOf({ normally_seconds: 200 * HOUR })).toBe('long');
    expect(lengthOf({ normally_seconds: 33501 * HOUR })).toBe('unknown');
  });

  it('keeps the games that bear every tag, and those of any length', () => {
    expect(names(withTags(all, ['Indie']))).toEqual(['Celeste', 'Hades']);
    expect(names(withTags(all, ['Indie', 'Role-playing (RPG)']))).toEqual(['Hades']);
    expect(names(withTags(all, []))).toEqual(['Celeste', 'Hades', 'Disco Elysium']);
    expect(names(withLengths(all, ['short', 'unknown']))).toEqual(['Celeste', 'Disco Elysium']);
    expect(lengthCounts(all)).toEqual({ short: 1, medium: 1, long: 0, unknown: 1 });
  });

  it('reads the address once per tag and per length, in order, what it does not know left out', () => {
    expect(askedTags([' Indie ', 'Indie', '', 'Action'])).toEqual(['Indie', 'Action']);
    expect(askedLengths(['unknown', 'short', 'huge'])).toEqual(['short', 'unknown']);
  });

  it('counts the tags of one kind, the picked first even when none of the games bears them', () => {
    expect(tagCounts(all, all, 'genres', [])).toEqual([
      { tag: 'Indie', count: 2 },
      { tag: 'Role-playing (RPG)', count: 2 },
      { tag: 'Platform', count: 1 },
    ]);
    expect(tagCounts([disco], all, 'genres', ['Platform'])).toEqual([
      { tag: 'Platform', count: 0 },
      { tag: 'Role-playing (RPG)', count: 1 },
    ]);
    // A theme picked is not offered among the genres.
    expect(tagCounts([disco], all, 'genres', ['Action'])).toEqual([
      { tag: 'Role-playing (RPG)', count: 1 },
    ]);
  });

  it('shortens the long names a chip shows, and leaves the others', () => {
    expect(shortTag('Role-playing (RPG)')).toBe('RPG');
    expect(shortTag('Indie')).toBe('Indie');
  });
});
