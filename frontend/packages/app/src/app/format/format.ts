/**
 * How the app writes a figure, a duration and a moment, in the reader's language. The words
 * around them are translations; these only give the part a language formats on its own.
 */

/** A count, with the language's own grouping: 1,305 or 1 305. */
export function formatCount(count: number, language: string): string {
  return new Intl.NumberFormat(language).format(count);
}

/** A playtime as the app says it: whole hours, or minutes under one hour. */
export interface Playtime {
  readonly unit: 'hours' | 'minutes';
  readonly count: number;
}

export function playtime(seconds: number): Playtime {
  const minutes = Math.round(seconds / 60);
  return minutes < 60
    ? { unit: 'minutes', count: minutes }
    : { unit: 'hours', count: Math.round(seconds / 3600) };
}

/** A moment, to the minute, in the reader's own time zone: "20 Nov 2026, 05:02". */
export function formatMoment(moment: string, language: string): string {
  return new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeStyle: 'short' }).format(
    new Date(moment),
  );
}

const DAY = 24 * 60 * 60 * 1000;

/** The start of a moment's day, on the reader's own calendar. */
function dayOf(moment: number): number {
  const date = new Date(moment);
  return new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
}

/**
 * How long ago a moment was, as the language says it and no finer than the day: "Today",
 * "Yesterday", "3 days ago", then weeks, months and years. The days are the reader's
 * calendar's: last night is yesterday, however few hours ago. A moment after `now` is today.
 */
export function formatAgo(moment: string, now: number, language: string): string {
  // Rounded: a day is an hour short or long when the clocks change.
  const days = Math.max(0, Math.round((dayOf(now) - dayOf(Date.parse(moment))) / DAY));
  const said = new Intl.RelativeTimeFormat(language, { numeric: 'auto' });
  const text =
    days < 14
      ? said.format(-days, 'day')
      : days < 60
        ? said.format(-Math.round(days / 7), 'week')
        : days < 365
          ? said.format(-Math.round(days / 30), 'month')
          : said.format(-Math.floor(days / 365), 'year');
  return text.charAt(0).toLocaleUpperCase(language) + text.slice(1);
}
