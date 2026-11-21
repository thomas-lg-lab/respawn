import { formatCount, formatMoment, playtime } from './format';

describe('format', () => {
  it('groups a count the way the language does', () => {
    expect(formatCount(1305, 'en')).toBe('1,305');
    // French groups with a narrow no-break space.
    expect(formatCount(1305, 'fr')).toMatch(/^1\s305$/);
  });

  it('says a playtime in whole hours, or in minutes under one hour', () => {
    expect(playtime(0)).toEqual({ unit: 'minutes', count: 0 });
    expect(playtime(45 * 60)).toEqual({ unit: 'minutes', count: 45 });
    expect(playtime(3600)).toEqual({ unit: 'hours', count: 1 });
    expect(playtime(2203200)).toEqual({ unit: 'hours', count: 612 });
  });

  it('writes a moment to the minute, in the language', () => {
    expect(formatMoment('2026-11-20T04:02:05Z', 'en')).toContain('Nov');
    expect(formatMoment('2026-11-20T04:02:05Z', 'fr')).toContain('nov.');
  });
});
