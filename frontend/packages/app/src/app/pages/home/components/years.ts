import { Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Card, ChartColumn, ColumnChart } from '@respawn/ui';
import { saidPlaytime } from '@app/format/format';
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
 * The time played year by year, whole years only: those Steam's Year in Review covers, and any
 * the syncs covered entirely. Each caption names the years it speaks of; the year still counted
 * in part is not drawn, and a line says when it comes.
 */
@Component({
  selector: 'rs-years',
  imports: [TranslocoDirective, Card, ChartColumn, ColumnChart],
  templateUrl: './years.html',
})
export class Years {
  readonly years = input.required<readonly PlayedYear[]>();
  /** The year left out because it is counted only in part; null when there is none. */
  readonly pending = input<number | null>(null);
  readonly language = input.required<string>();

  /** The reviewed years, as runs; null when none is. */
  protected readonly reviewed = computed(() => {
    const reviewed = this.years().filter((each) => each.reviewed);
    return reviewed.length ? yearRuns(reviewed.map((each) => each.year)) : null;
  });

  /** The whole years the syncs counted, as runs; null when none is theirs. */
  protected readonly synced = computed(() => {
    const synced = this.years().filter((each) => !each.reviewed);
    return synced.length ? yearRuns(synced.map((each) => each.year)) : null;
  });

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
