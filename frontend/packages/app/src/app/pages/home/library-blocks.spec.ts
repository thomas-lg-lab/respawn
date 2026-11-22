import type { LibraryEntry, LibraryGame } from '@respawn/data';
import { drawn, lastPlayed, neverLaunched, playedRecently, ranking } from './library-blocks';

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

const names = (games: readonly LibraryGame[]) => games.map((each) => each.name);

const hades = game('Hades', {
  playtime_seconds: 360000,
  recent_playtime_seconds: 3600,
  last_played_at: '2026-11-10T20:00:00Z',
});
const portal = game('Portal 2', {
  playtime_seconds: 7200,
  recent_playtime_seconds: 7200,
  last_played_at: '2026-11-19T20:00:00Z',
});
const tunic = game('Tunic', {
  playtime_seconds: 600,
  recent_playtime_seconds: 0,
  last_played_at: '2025-01-02T20:00:00Z',
});
const celeste = game('Celeste');
const unknown = game('Pokémon', { playtime_seconds: null, recent_playtime_seconds: null });
const games = [celeste, hades, unknown, portal, tunic];

describe('the blocks of the home', () => {
  it('resumes the game whose last session is the latest', () => {
    expect(lastPlayed(games, 'en')).toBe(portal);
  });

  it('resumes nothing while no store told a session', () => {
    expect(lastPlayed([celeste, unknown], 'en')).toBeNull();
  });

  it('lists the games of the last two weeks, the most played first, without the resumed one', () => {
    const also = game('Also', { playtime_seconds: 9000, recent_playtime_seconds: 5400 });

    const played = playedRecently([...games, also], portal, 'en');

    expect(played.by).toBe('recent');
    expect(names(played.games)).toEqual(['Also', 'Hades']);
  });

  it('keeps the titles in order between two games played as much lately', () => {
    const b = game('B', { recent_playtime_seconds: 60 });
    const a = game('A', { recent_playtime_seconds: 60 });

    expect(names(playedRecently([b, a], null, 'en').games)).toEqual(['A', 'B']);
  });

  it('shows four games at most', () => {
    const many = Array.from({ length: 6 }, (_, index) =>
      game(`G${index}`, { recent_playtime_seconds: 60 * (index + 1) }),
    );

    expect(playedRecently(many, null, 'en').games).toHaveLength(4);
  });

  it('falls back on the latest sessions when no game of the two weeks is named', () => {
    const quiet = games.map((each) => ({ ...each, recent_playtime_seconds: null }));
    const resumed = quiet.find((each) => each.name === 'Portal 2')!;

    const played = playedRecently(quiet, resumed, 'en');

    expect(played.by).toBe('last');
    expect(names(played.games)).toEqual(['Hades', 'Tunic']);
  });

  it('ranks the launched games by playtime and sums up the ones it does not name', () => {
    const more = Array.from({ length: 6 }, (_, index) =>
      game(`G${index}`, { playtime_seconds: 1000 + index }),
    );

    const ranked = ranking([...games, ...more], 'en');

    expect(names(ranked.top)).toEqual(['Hades', 'Portal 2', 'G5', 'G4', 'G3', 'G2']);
    expect(ranked.seconds).toBe(360000 + 7200 + 600 + 6015);
    // Tunic and the two least played: a game never launched, or unknown, is not among them.
    expect(ranked.others).toEqual({ count: 3, seconds: 600 + 1000 + 1001 });
  });

  it('names every launched game when there are few, with nothing left to sum up', () => {
    expect(ranking(games, 'en').others).toEqual({ count: 0, seconds: 0 });
  });

  it('draws among the games never launched that a store still lists', () => {
    const gone = game('Gone', { entries: [entry('2026-11-19T04:00:00Z')] });
    const kept = game('Kept', { entries: [entry(null)] });

    // One whose playtime is unknown counts as never launched, as in the Library's filter.
    expect(names(neverLaunched([...games, gone, kept]))).toEqual(['Celeste', 'Pokémon', 'Kept']);
  });

  it('draws each game once, and all of them when there are no more', () => {
    const always = (value: number) => () => value;

    expect(drawn(['a', 'b', 'c', 'd'], 3, always(0))).toEqual(['a', 'b', 'c']);
    expect(drawn(['a', 'b', 'c', 'd'], 3, always(0.99))).toEqual(['d', 'c', 'b']);
    expect(drawn(['a', 'b'], 3, always(0.5))).toEqual(['b', 'a']);
    expect(drawn([], 3)).toEqual([]);
  });
});
