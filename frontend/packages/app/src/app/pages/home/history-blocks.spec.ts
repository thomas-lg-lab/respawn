import type { LibraryGame, LibraryHistory } from '@respawn/data';
import { activity, playedYears } from './history-blocks';

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

const day = (date: string, ...played: [string, number][]) => ({
  date,
  games: played.map(([name, seconds]) => ({ game_id: `id-${name}`, seconds })),
});

const at = (moment: string) => Date.parse(moment);

describe('the blocks of the home made of the history', () => {
  describe('the years', () => {
    it('adds up each year, from the one of the first sync to the current one', () => {
      const history: LibraryHistory = {
        tracking_since: '2026-11-18T10:05:00Z',
        days: [
          day('2026-11-20', ['a', 3600], ['b', 1800]),
          day('2026-12-31', ['a', 600]),
          day('2027-01-01', ['a', 7200]),
          day('2029-02-03', ['b', 60]),
        ],
      };

      expect(playedYears(history, at('2029-03-01T00:00:00Z'))).toEqual([
        { year: 2026, seconds: 6000, open: true },
        { year: 2027, seconds: 7200, open: false },
        // Nothing played that year: it is there all the same.
        { year: 2028, seconds: 0, open: false },
        { year: 2029, seconds: 60, open: true },
      ]);
    });

    it('goes on to a year nothing was played in yet', () => {
      const history: LibraryHistory = {
        tracking_since: '2026-11-18T10:05:00Z',
        days: [day('2026-11-20', ['a', 3600])],
      };

      expect(playedYears(history, at('2027-01-02T00:00:00Z'))).toEqual([
        { year: 2026, seconds: 3600, open: true },
        { year: 2027, seconds: 0, open: true },
      ]);
    });

    it('counts a year in UTC, as the days are', () => {
      const history: LibraryHistory = {
        tracking_since: '2026-12-31T23:30:00Z',
        days: [day('2027-01-01', ['a', 3600])],
      };

      expect(playedYears(history, at('2027-01-01T00:30:00Z')).map((each) => each.year)).toEqual([
        2026, 2027,
      ]);
    });

    it('keeps a year the history has when the clock is behind it', () => {
      const history: LibraryHistory = {
        tracking_since: '2026-12-30T10:00:00Z',
        days: [day('2027-01-01', ['a', 3600])],
      };

      expect(playedYears(history, at('2026-12-31T23:59:00Z')).map((each) => each.year)).toEqual([
        2026, 2027,
      ]);
    });

    it('has none while nothing was played since the first sync, or no sync was', () => {
      expect(playedYears({ tracking_since: '2026-11-18T10:05:00Z', days: [] }, Date.now())).toEqual(
        [],
      );
      expect(playedYears({ tracking_since: null, days: [] }, Date.now())).toEqual([]);
    });
  });

  describe('the activity', () => {
    const games = [game('a', { playtime_seconds: 36000 }), game('b')];

    it('gives the latest five days, the latest first', () => {
      const history: LibraryHistory = {
        tracking_since: '2026-11-01T00:00:00Z',
        days: [1, 2, 3, 4, 5, 6, 7].map((each) => day(`2026-11-0${each}`, ['a', 60])),
      };

      expect(activity(history, games, true).map((each) => each.date)).toEqual([
        '2026-11-07',
        '2026-11-06',
        '2026-11-05',
        '2026-11-04',
        '2026-11-03',
      ]);
    });

    it('names the games of a day as the library does, in the order the API gave', () => {
      const history: LibraryHistory = {
        tracking_since: '2026-11-01T00:00:00Z',
        days: [day('2026-11-02', ['a', 3600], ['b', 60])],
      };

      expect(activity(history, games, true)).toEqual([
        {
          date: '2026-11-02',
          games: [
            { id: 'id-a', game: games[0], gone: false, seconds: 3600 },
            { id: 'id-b', game: games[1], gone: false, seconds: 60 },
          ],
          others: 0,
        },
      ]);
    });

    it('names four games of a day and counts the others', () => {
      const history: LibraryHistory = {
        tracking_since: '2026-11-01T00:00:00Z',
        days: [day('2026-11-02', ['a', 6], ['b', 5], ['c', 4], ['d', 3], ['e', 2], ['f', 1])],
      };
      const [only] = activity(history, games, true);

      expect(only?.games.map((each) => each.id)).toEqual(['id-a', 'id-b', 'id-c', 'id-d']);
      expect(only?.others).toBe(2);
    });

    it('keeps a game the library no longer has, without a name', () => {
      const history: LibraryHistory = {
        tracking_since: '2026-11-01T00:00:00Z',
        days: [day('2026-11-02', ['gone', 600])],
      };

      expect(activity(history, games, true)[0]?.games).toEqual([
        { id: 'id-gone', game: null, gone: true, seconds: 600 },
      ]);
    });

    it('does not say gone a game that games still asked again do not have', () => {
      const history: LibraryHistory = {
        tracking_since: '2026-11-01T00:00:00Z',
        days: [day('2026-11-02', ['new', 600], ['a', 60])],
      };

      expect(activity(history, games, false)[0]?.games).toEqual([
        { id: 'id-new', game: null, gone: false, seconds: 600 },
        { id: 'id-a', game: games[0], gone: false, seconds: 60 },
      ]);
    });
  });
});
