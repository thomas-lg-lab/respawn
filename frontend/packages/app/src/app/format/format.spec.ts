import { formatAgo, formatCount, formatDate, formatDay, formatMoment, playtime } from './format';

describe('format', () => {
  it('groups a count the way the language does', () => {
    expect(formatCount(1305, 'en')).toBe('1,305');
    // French groups with a narrow no-break space.
    expect(formatCount(1305, 'fr')).toMatch(/^1\s305$/);
  });

  it('says a playtime in whole hours, or in minutes under one hour', () => {
    expect(playtime(0)).toEqual({ unit: 'minutes', count: 0 });
    expect(playtime(20)).toEqual({ unit: 'minutes', count: 1 });
    expect(playtime(45 * 60)).toEqual({ unit: 'minutes', count: 45 });
    expect(playtime(3600)).toEqual({ unit: 'hours', count: 1 });
    expect(playtime(2203200)).toEqual({ unit: 'hours', count: 612 });
  });

  it('writes a moment to the minute, in the language', () => {
    expect(formatMoment('2026-11-20T04:02:05Z', 'en')).toContain('Nov');
    expect(formatMoment('2026-11-20T04:02:05Z', 'fr')).toContain('nov.');
  });

  it('says how long ago a moment was, no finer than the day', () => {
    const now = Date.parse('2026-11-21T12:00:00Z');
    const ago = (days: number, language = 'en') =>
      formatAgo(new Date(now - days * 24 * 60 * 60 * 1000).toISOString(), now, language);

    expect(ago(0)).toBe('Today');
    expect(ago(1)).toBe('Yesterday');
    expect(ago(3)).toBe('3 days ago');
    expect(ago(21)).toBe('3 weeks ago');
    expect(ago(90)).toBe('3 months ago');
    expect(ago(800)).toBe('2 years ago');
    // A year is counted: "last year" would be the calendar's.
    expect(ago(400)).toBe('1 year ago');
    expect(ago(400, 'fr')).toBe('Il y a 1 an');
    expect(ago(1, 'fr')).toBe('Hier');
    expect(ago(3, 'fr')).toBe('Il y a 3 jours');
    // Last night is yesterday, however few hours ago.
    const morning = new Date(2026, 10, 21, 9).getTime();
    const lastNight = new Date(2026, 10, 20, 23).toISOString();
    expect(formatAgo(lastNight, morning, 'en')).toBe('Yesterday');
    // A clock that runs behind the server's is no reason to speak of the future.
    expect(ago(-1)).toBe('Today');
  });

  it('writes the day of a moment', () => {
    expect(formatDate('2026-11-18T10:05:00Z', 'en')).toBe('Nov 18, 2026');
    expect(formatDate('2026-11-18T10:05:00Z', 'fr')).toBe('18 nov. 2026');
    // In UTC, as the years of the history are: the last evening of a year stays in it.
    expect(formatDate('2026-12-31T23:30:00Z', 'en')).toBe('Dec 31, 2026');
    expect(formatDate('2027-01-01T00:30:00Z', 'en')).toBe('Jan 1, 2027');
  });

  it('writes a day the API gave with its weekday, as it is in UTC', () => {
    expect(formatDay('2026-11-21', 'en')).toBe('Saturday, November 21');
    expect(formatDay('2026-11-21', 'fr')).toBe('Samedi 21 novembre');
    // The first of the month stays so, whatever the reader's time zone.
    expect(formatDay('2026-12-01', 'en')).toBe('Tuesday, December 1');
  });
});
