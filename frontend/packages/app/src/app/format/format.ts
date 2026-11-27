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

const plurals = perLanguage((language) => new Intl.PluralRules(language));
const wholeShares = perLanguage(
  (language) => new Intl.NumberFormat(language, { style: 'percent', maximumFractionDigits: 0 }),
);
const smallShares = perLanguage(
  (language) => new Intl.NumberFormat(language, { style: 'percent', maximumFractionDigits: 1 }),
);
const titles = perLanguage(
  (language) => new Intl.Collator(language, { sensitivity: 'base', numeric: true }),
);

/** The order of two titles in the language: no case, no accent, "Part 2" before "Part 10". */
export function compareTitles(a: string, b: string, language: string): number {
  return titles(language).compare(a, b);
}

/** A share of a whole, as a percentage: whole, unless that says nothing of a small one. */
export function formatShare(fraction: number, language: string): string {
  return (fraction > 0 && fraction < 0.005 ? smallShares : wholeShares)(language).format(fraction);
}

/** Whether a count takes the singular in the language: 1 in English, 0 and 1 in French. */
export function isOne(count: number, language: string): boolean {
  return plurals(language).select(count) === 'one';
}

/** A count, with the language's own grouping: 1,305 or 1 305. */
export function formatCount(count: number, language: string): string {
  return counts(language).format(count);
}

/** A playtime as the app says it: whole hours, or minutes under one hour. */
export interface Playtime {
  readonly unit: 'hours' | 'minutes';
  readonly count: number;
}

/** A game played for a few seconds was still played: it says one minute, never none. */
export function playtime(seconds: number): Playtime {
  const minutes = seconds > 0 ? Math.max(1, Math.round(seconds / 60)) : 0;
  return minutes < 60
    ? { unit: 'minutes', count: minutes }
    : { unit: 'hours', count: Math.round(seconds / 3600) };
}

/**
 * A playtime as a translation says it: the key of its unit, the same on every screen, and
 * its count in the language.
 */
export interface SaidPlaytime {
  readonly key: 'duration.hours' | 'duration.minutes';
  readonly count: string;
}

export function saidPlaytime(seconds: number, language: string): SaidPlaytime {
  const { unit, count } = playtime(seconds);
  return { key: `duration.${unit}`, count: formatCount(count, language) };
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

const utcDates = perLanguage(
  (language) => new Intl.DateTimeFormat(language, { dateStyle: 'medium', timeZone: 'UTC' }),
);
const utcDays = perLanguage(
  (language) =>
    new Intl.DateTimeFormat(language, {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      timeZone: 'UTC',
    }),
);

/**
 * The day of a moment, in UTC: "Nov 18, 2026". It is written beside days and years the API
 * counts in UTC (the history), and names the same ones: the reader's time zone would put a
 * first sync late on 31 December in the year after the one its column is drawn in.
 */
export function formatDate(moment: string, language: string): string {
  return utcDates(language).format(new Date(moment));
}

/**
 * A day the API gave as `YYYY-MM-DD`, with its weekday: "Saturday, November 21". The day is
 * UTC's and is written as it is: no time zone moves it to the one before.
 */
export function formatDay(day: string, language: string): string {
  const text = utcDays(language).format(new Date(`${day}T00:00:00Z`));
  return text.charAt(0).toLocaleUpperCase(language) + text.slice(1);
}

const ordinals = perLanguage((language) => new Intl.PluralRules(language, { type: 'ordinal' }));

/**
 * The form a rank takes in the language, for a translation keyed by it: `one` for 1st and
 * 1er, `two` for 2nd, `few` for 3rd, `other` for the rest.
 */
export function ordinalForm(rank: number, language: string): Intl.LDMLPluralRule {
  return ordinals(language).select(rank);
}
