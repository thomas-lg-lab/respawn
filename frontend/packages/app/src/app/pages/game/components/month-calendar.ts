import {
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { saidPlaytime } from '@app/format/format';
import type { Calendar, CalendarCell, MonthKey } from '../game-view';

const SHADES = [1, 2, 3, 4] as const;

const shortMonths = new Map<string, string[]>();
const longMonths = new Map<string, Intl.DateTimeFormat>();

/** The twelve months' names, short, as the language writes them: "janv", "févr". */
function monthNames(language: string): string[] {
  let names = shortMonths.get(language);
  if (!names) {
    const format = new Intl.DateTimeFormat(language, { month: 'short', timeZone: 'UTC' });
    names = Array.from({ length: 12 }, (_, index) =>
      format.format(Date.UTC(2026, index, 15)).replace(/\.$/, ''),
    );
    shortMonths.set(language, names);
  }
  return names;
}

/** A month and its year, whole: "mars 2022". */
export function monthName(month: MonthKey, language: string): string {
  let format = longMonths.get(language);
  if (!format) {
    format = new Intl.DateTimeFormat(language, { month: 'long', year: 'numeric', timeZone: 'UTC' });
    longMonths.set(language, format);
  }
  return format.format(new Date(`${month}-15T00:00:00Z`));
}

/**
 * What was played of a game month by month: a row of twelve cells per year, the years with
 * nothing folded into one line. A played month is as dark as its share of the game's best
 * month, so a game of 2,000 hours and one of 12 both show their shape. A played month can be
 * pressed: the page says what it holds under the calendar (projected). On a phone the
 * calendar slides sideways, the year staying in view, and opens on the latest months.
 */
@Component({
  selector: 'rs-month-calendar',
  imports: [TranslocoDirective],
  templateUrl: './month-calendar.html',
})
export class MonthCalendar {
  readonly calendar = input.required<Calendar>();
  /** The month pressed, shown under the calendar. */
  readonly selected = input<MonthKey | null>(null);
  readonly language = input.required<string>();
  readonly picked = output<MonthKey>();

  protected readonly shades = SHADES;
  protected readonly months = computed(() => monthNames(this.language()));

  /** Whether a month nothing counted is drawn: the line under the calendar says what that is. */
  protected readonly uncounted = computed(() =>
    this.calendar().rows.some(
      (row) => row.kind === 'year' && row.cells.some((cell) => cell.kind === 'uncounted'),
    ),
  );

  private readonly slider = viewChild.required<ElementRef<HTMLElement>>('slider');

  constructor() {
    // Opened on the latest months: where it slides, the current one is at its end. Again
    // whenever it changes width, so a window narrowed to a phone's opens it there too.
    const destroyed = inject(DestroyRef);
    afterNextRender(() => {
      const slider = this.slider().nativeElement;
      const toEnd = () => (slider.scrollLeft = slider.scrollWidth);
      toEnd();
      if (typeof ResizeObserver === 'undefined') return;
      const resized = new ResizeObserver(toEnd);
      resized.observe(slider);
      destroyed.onDestroy(() => resized.disconnect());
    });
  }

  protected said(seconds: number) {
    return saidPlaytime(seconds, this.language());
  }

  protected name(cell: CalendarCell): string {
    return monthName(cell.month, this.language());
  }
}
