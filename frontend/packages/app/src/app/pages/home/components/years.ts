import { Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Card, ChartColumn, ColumnChart } from '@respawn/ui';
import { formatDate, saidPlaytime } from '@app/format/format';
import type { PlayedYear } from '../history-blocks';

/**
 * Years as runs: "2024", "2022–2023, 2025". A year left out of a run is one the chart does not
 * draw, or draws from the other source: each caption names exactly its own years.
 */
export function yearRuns(years: readonly number[]): string {
  const runs: [number, number][] = [];
  for (const year of [...years].sort((a, b) => a - b)) {
    const last = runs.at(-1);
    if (last && year === last[1] + 1) last[1] = year;
    else runs.push([year, year]);
  }
  return runs
    .map(([first, last]) => (first === last ? String(first) : `${first}–${last}`))
    .join(', ');
}

/**
 * The time played year by year: the years Steam's Year in Review covers, whole, and the years
 * counted day by day at each sync. Each caption names the years it speaks of, and a year that is
 * not whole is drawn hatched (the one the first sync fell in, the one still running).
 */
@Component({
  selector: 'rs-years',
  imports: [TranslocoDirective, Card, ChartColumn, ColumnChart],
  templateUrl: './years.html',
})
export class Years {
  readonly years = input.required<readonly PlayedYear[]>();
  readonly language = input.required<string>();

  /** The reviewed years, as runs; null when none is. */
  protected readonly reviewed = computed(() => {
    const reviewed = this.years().filter((each) => each.reviewed);
    return reviewed.length ? yearRuns(reviewed.map((each) => each.year)) : null;
  });

  /** The years the syncs count, as runs, and the first day they count; null when none is theirs. */
  protected readonly synced = computed(() => {
    const synced = this.years().filter((each) => !each.reviewed);
    const first = synced.at(0);
    return first
      ? {
          years: yearRuns(synced.map((each) => each.year)),
          date: formatDate(first.firstDay, this.language()),
        }
      : null;
  });

  protected readonly someOpen = computed(() => this.years().some((each) => each.open));

  /** A full bar is the most played year. */
  protected readonly scale = computed(() =>
    Math.max(0, ...this.years().map((each) => each.seconds)),
  );

  protected readonly columns = computed(() =>
    this.years().map((each) => ({
      ...each,
      label: String(each.year),
      time: saidPlaytime(each.seconds, this.language()),
    })),
  );
}
