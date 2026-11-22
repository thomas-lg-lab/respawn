/**
 * How the app writes a figure, a duration and a moment, in the reader's language. The words
 * around them are translations; these only give the part a language formats on its own.
 */

/**
 * One formatter per language, made once: a list formats a figure and a moment per row, and
 * making a formatter costs far more than using it.
 */
function perLanguage<T>(make: (language: string) => T): (language: string) => T {
  const made = new Map<string, T>();
  return (language) => {
    let formatter = made.get(language);
    if (formatter === undefined) {
      formatter = make(language);
      made.set(language, formatter);
    }
    return formatter;
  };
}

const counts = perLanguage((language) => new Intl.NumberFormat(language));
const moments = perLanguage(
  (language) => new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeStyle: 'short' }),
);
/** "Yesterday" rather than "1 day ago". */
const agoInWords = perLanguage(
  (language) => new Intl.RelativeTimeFormat(language, { numeric: 'auto' }),
);
/** Always a count: "1 year ago", since "last year" is the calendar's, not 365 days. */
const agoInFigures = perLanguage(
  (language) => new Intl.RelativeTimeFormat(language, { numeric: 'always' }),
);

/** A count, with the language's own grouping: 1,305 or 1 305. */
export function formatCount(count: number, language: string): string {
  return counts(language).format(count);
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
  return moments(language).format(new Date(moment));
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
 * calendar's: last night is yesterday, however few hours ago. Years are counted, "1 year ago":
 * "last year" would name the calendar's, which 365 days ago may not be. A moment after `now`
 * is today.
 */
export function formatAgo(moment: string, now: number, language: string): string {
  // Rounded: a day is an hour short or long when the clocks change.
  const days = Math.max(0, Math.round((dayOf(now) - dayOf(Date.parse(moment))) / DAY));
  const said = agoInWords(language);
  const text =
    days < 14
      ? said.format(-days, 'day')
      : days < 60
        ? said.format(-Math.round(days / 7), 'week')
        : days < 365
          ? said.format(-Math.round(days / 30), 'month')
          : agoInFigures(language).format(-Math.floor(days / 365), 'year');
  return text.charAt(0).toLocaleUpperCase(language) + text.slice(1);
}
