import type { LibraryGame, LibraryHistory, LibraryYear, LibraryYears } from '@respawn/data';
import { game as made } from '@app/testing/games';
import { activity, playedYears } from './history-blocks';

/** A game whose id says its name: the days of a history name their games by id. */
const game = (name: string, values: Partial<LibraryGame> = {}) =>
  made(name, { game_id: `id-${name}`, ...values });

const day = (date: string, ...played: [string, number][]) => ({
  date,
  games: played.map(([name, seconds]) => ({ game_id: `id-${name}`, seconds })),
});

describe('the blocks of the home made of the history', () => {
  describe('the years', () => {
    const year = (
      at: number,
      seconds: number,
      source: LibraryYear['source'],
      firstDay = `${at}-01-01`,
      whole = true,
    ): LibraryYear => ({
      year: at,
      seconds,
      source,
      first_day: firstDay,
      last_day: `${at}-12-31`,
      whole,
    });

    it('draws only the whole years, and leaves out the one counted in part', () => {
      const told: LibraryYears = {
        years: [
          year(2024, 11700, 'review'),
          // Reviewed, with nothing played: it is there all the same.
          year(2025, 0, 'review'),
          year(2026, 3600, 'readings', '2026-11-18', false),
        ],
      };

      expect(playedYears(told)).toEqual({
        years: [
          { year: 2024, seconds: 11700, reviewed: true },
          { year: 2025, seconds: 0, reviewed: true },
        ],
        pending: [2026],
      });
    });

    it('leaves out every year counted in part, not only the latest', () => {
      // Early 2027, before the review of 2026: the first sync's year and the one running.
      const told: LibraryYears = {
        years: [
          year(2025, 3600, 'review'),
          year(2026, 1800, 'readings', '2026-11-18', false),
          year(2027, 600, 'readings', '2027-01-01', false),
        ],
      };

      expect(playedYears(told)).toEqual({
        years: [{ year: 2025, seconds: 3600, reviewed: true }],
        pending: [2026, 2027],
      });
    });

    it('draws a year the syncs covered entirely, should no review come', () => {
      expect(playedYears({ years: [year(2027, 7200, 'readings')] })).toEqual({
        years: [{ year: 2027, seconds: 7200, reviewed: false }],
        pending: [],
      });
    });

    it('draws no year while no whole year has time, and still names the ones in part', () => {
      expect(playedYears({ years: [year(2026, 3600, 'readings', '2026-11-18', false)] })).toEqual({
        years: [],
        pending: [2026],
      });
    });

    it('has none while nothing was played in any year, or nothing was read', () => {
      expect(
        playedYears({
          years: [year(2025, 0, 'review'), year(2026, 0, 'readings', '2026-11-18', false)],
        }),
      ).toEqual({ years: [], pending: [] });
      expect(playedYears({ years: [] })).toEqual({ years: [], pending: [] });
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
          rest: [],
        },
      ]);
    });

    it('names four games of a day and counts the others, kept for when they are asked for', () => {
      const history: LibraryHistory = {
        tracking_since: '2026-11-01T00:00:00Z',
        days: [day('2026-11-02', ['a', 6], ['b', 5], ['c', 4], ['d', 3], ['e', 2], ['f', 1])],
      };
      const [only] = activity(history, games, true);

      expect(only?.games.map((each) => each.id)).toEqual(['id-a', 'id-b', 'id-c', 'id-d']);
      expect(only?.rest.map((each) => each.id)).toEqual(['id-e', 'id-f']);
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
