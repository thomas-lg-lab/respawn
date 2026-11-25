import { Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Card, ChartColumn, ColumnChart } from '@respawn/ui';
import { formatDate, saidPlaytime } from '@app/format/format';
import type { PlayedYear } from '../history-blocks';

/**
 * The time played year by year: the years Steam's Year in Review covers, whole, then the years
 * from the first sync on, counted day by day. The block says where each part comes from, and
 * draws as an outline a year that is not whole (the one the first sync fell in, the one still
 * running).
 */
@Component({
  selector: 'rs-years',
  imports: [TranslocoDirective, Card, ChartColumn, ColumnChart],
  templateUrl: './years.html',
})
export class Years {
  readonly years = input.required<readonly PlayedYear[]>();
  readonly language = input.required<string>();

  /** The reviewed years, as one span: "2024", or "2022–2025"; null when none is. */
  protected readonly reviewed = computed(() => {
    const reviewed = this.years().filter((each) => each.reviewed);
    const [first, last] = [reviewed.at(0), reviewed.at(-1)];
    if (!first || !last) return null;
    return first === last ? String(first.year) : `${first.year}–${last.year}`;
  });

  /** The first day the syncs count, when some year is theirs. */
  protected readonly since = computed(() => {
    const counted = this.years().find((each) => !each.reviewed);
    return counted ? formatDate(counted.firstDay, this.language()) : null;
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
