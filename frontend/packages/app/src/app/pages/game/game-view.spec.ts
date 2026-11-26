import type { GameMonth, LibraryMonth, LibraryYear } from '@respawn/data';
import { type CalendarYear, calendar, monthAt, monthDetail, secondsIn } from './game-view';

const HOUR = 3600;
/** 26 November 2026, noon UTC: November is the current month. */
const NOW = Date.UTC(2026, 10, 26, 12);

function month(key: string, hours: number): GameMonth {
  return { month: `${key}-01`, seconds: hours * HOUR, source: 'review' };
}

/** A whole year from a review. */
function reviewed(year: number): LibraryYear {
  return {
    year,
    seconds: 600 * HOUR,
    source: 'review',
    first_day: `${year}-01-01`,
    last_day: `${year}-12-31`,
    whole: true,
  };
}

/** The current year, counted from the first sync on. */
const READ_2026: LibraryYear = {
  year: 2026,
  seconds: 38 * HOUR,
  source: 'readings',
  first_day: '2026-11-21',
  last_day: '2026-11-26',
  whole: false,
};

const YEARS = [reviewed(2022), reviewed(2023), reviewed(2024), reviewed(2025), READ_2026];

describe('the calendar of a game', () => {
  const rowOf = (drawn: ReturnType<typeof calendar>, year: number) =>
    drawn.rows.find((row): row is CalendarYear => row.kind === 'year' && row.year === year)!;

  it('draws a row per year from the first played to the current one, folding the empty ones', () => {
    const drawn = calendar(
      [month('2022-03', 21), month('2023-07', 7), month('2026-11', 6)],
      YEARS,
      34 * HOUR,
      NOW,
    );

    expect(drawn.rows.map((row) => (row.kind === 'year' ? row.year : row))).toEqual([
      2022,
      2023,
      { kind: 'gap', from: 2024, to: 2025, said: 'noSession' },
      2026,
    ]);
    expect(rowOf(drawn, 2022).seconds).toBe(21 * HOUR);
    expect(drawn.last).toBe('2026-11');
    expect(drawn.best?.month).toBe('2022-03');
  });

  it('says nothing was played since, at the end', () => {
    const drawn = calendar([month('2023-02', 4)], YEARS, 4 * HOUR, NOW);

    expect(drawn.rows.at(-1)).toEqual({ kind: 'gap', from: 2024, to: 2026, said: 'nothingSince' });
  });

  it('tells a month with no session from one nothing counted, and from one to come', () => {
    const drawn = calendar([month('2026-11', 6)], YEARS, 6 * HOUR, NOW);
    const kinds = rowOf(drawn, 2026).cells.map((cell) => cell.kind);

    // Nothing counted 2026 before the first sync, on 21 November; December is to come.
    expect(kinds).toEqual([...Array(10).fill('uncounted'), 'played', 'later']);

    const reviewedOnly = calendar([month('2025-06', 2)], YEARS, 2 * HOUR, NOW);
    expect(rowOf(reviewedOnly, 2025).cells[0]?.kind).toBe('none');
  });

  it('says a folded year was not counted when nothing counted any of its months', () => {
    const drawn = calendar(
      [month('2022-01', 3), month('2026-11', 1)],
      [reviewed(2022), READ_2026],
      4 * HOUR,
      NOW,
    );

    expect(drawn.rows[1]).toEqual({ kind: 'gap', from: 2023, to: 2025, said: 'notCounted' });
  });

  it('shades a month on its share of the best one, whatever the game’s size', () => {
    for (const scale of [1, 100]) {
      const drawn = calendar(
        [
          month('2025-01', 1 * scale),
          month('2025-02', 10 * scale),
          month('2025-03', 15 * scale),
          month('2025-04', 20 * scale),
        ],
        YEARS,
        null,
        NOW,
      );
      expect(
        rowOf(drawn, 2025)
          .cells.slice(0, 4)
          .map((cell) => cell.shade),
      ).toEqual([1, 2, 3, 4]);
    }
  });

  it('says the time no month holds, from an hour on', () => {
    const months = [month('2025-05', 10)];

    expect(calendar(months, YEARS, 1121 * HOUR, NOW).unplaced).toBe(1111 * HOUR);
    // Less than an hour apart: the months may just round it away.
    expect(calendar(months, YEARS, 10 * HOUR + 1800, NOW).unplaced).toBe(0);
    expect(calendar(months, YEARS, null, NOW).unplaced).toBe(0);
  });

  it('has no row for a game no month holds', () => {
    expect(calendar([], YEARS, 5 * HOUR, NOW)).toEqual({
      rows: [],
      best: null,
      last: null,
      unplaced: 5 * HOUR,
    });
  });

  it('counts a year and names the current month in UTC', () => {
    expect(secondsIn([month('2025-05', 10), month('2026-01', 2)], 2025)).toBe(10 * HOUR);
    expect(monthAt(Date.UTC(2026, 0, 1, 0, 30))).toBe('2026-01');
  });
});

describe('a month seen from a game', () => {
  const OWN = 'own';
  function played(...games: [id: string, hours: number][]): LibraryMonth {
    return {
      month: '2026-03-01',
      source: 'review',
      first_day: null,
      last_day: null,
      games: games.map(([id, hours]) => ({ game_id: id, name: id, seconds: hours * HOUR })),
    };
  }

  it('ranks the game among those of the month, and lists the most played', () => {
    const detail = monthDetail(
      played(['a', 22], [OWN, 6], ['b', 5], ['c', 3], ['d', 2], ['e', 1]),
      OWN,
      6 * HOUR,
    );

    expect(detail.rank).toBe(2);
    expect(detail.count).toBe(6);
    expect(detail.total).toBe(39 * HOUR);
    expect(detail.rows.map((row) => row.id)).toEqual(['a', OWN, 'b', 'c']);
    expect(detail.others).toEqual({ count: 2, seconds: 3 * HOUR });
    expect(detail.scale).toBe(22 * HOUR);
  });

  it('keeps the game in the list when it is not among the most played', () => {
    const detail = monthDetail(played(['a', 9], ['b', 8], ['c', 7], ['d', 6], [OWN, 1]), OWN, HOUR);

    expect(detail.rank).toBe(5);
    expect(detail.rows.map((row) => row.id)).toEqual(['a', 'b', 'c', OWN]);
    expect(detail.others).toEqual({ count: 1, seconds: 6 * HOUR });
  });

  it('counts the game from its own months when the month does not name it', () => {
    const detail = monthDetail(played(['a', 3]), OWN, 2 * HOUR);

    expect(detail.rank).toBe(2);
    expect(detail.seconds).toBe(2 * HOUR);
    expect(detail.rows.map((row) => row.own)).toEqual([false, true]);
  });
});
