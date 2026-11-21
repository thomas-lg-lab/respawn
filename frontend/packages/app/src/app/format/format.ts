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
