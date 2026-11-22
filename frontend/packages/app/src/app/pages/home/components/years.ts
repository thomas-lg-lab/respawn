import { Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Card, ChartColumn, ColumnChart } from '@respawn/ui';
import { formatDate, saidPlaytime } from '@app/format/format';
import type { PlayedYear } from '../history-blocks';

/**
 * The time played year by year, as the history tells it: from the first sync on. The block
 * says since when it counts, and draws as an outline a year that is not whole (the one the
 * first sync fell in, the one still running).
 */
@Component({
  selector: 'rs-years',
  imports: [TranslocoDirective, Card, ChartColumn, ColumnChart],
  templateUrl: './years.html',
})
export class Years {
  readonly years = input.required<readonly PlayedYear[]>();
  /** When the history starts: the first sync. */
  readonly since = input.required<string>();
  readonly language = input.required<string>();

  protected readonly sinceDate = computed(() => formatDate(this.since(), this.language()));

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
