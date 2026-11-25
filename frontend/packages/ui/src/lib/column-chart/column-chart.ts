import { Component, booleanAttribute, computed, input } from '@angular/core';

/**
 * How tall a bar is, in hundredths of the height, for `value` out of `max`. Anything above
 * zero is drawn, so "a little" never reads as "nothing".
 */
export function barHeight(value: number, max: number): number {
  if (!(value > 0) || !(max > 0)) return 0;
  return Math.min(100, Math.max(2, Math.round((value / max) * 100)));
}

/**
 * A few values side by side as columns: a list, each item a `rs-chart-column`. A column
 * keeps its width, so one alone does not stretch over the card; more than the width holds
 * are scrolled to.
 */
@Component({
  selector: 'rs-column-chart',
  host: { role: 'list' },
  template: `<ng-content />`,
  styleUrl: './column-chart.css',
})
export class ColumnChart {}

/**
 * One column of a `rs-column-chart`: a bar as tall as `value` is of `max`, the value in
 * words above it (projected) and what it measures under it. The bar is a drawing, hidden
 * from screen readers: the column is read by its words. `open` is a value that is not whole
 * yet, a year still running: drawn paler, and said by `openLabel` to who does not
 * see the drawing. A value of zero draws no bar.
 */
@Component({
  selector: 'rs-chart-column',
  host: { role: 'listitem', '[attr.data-open]': "open() ? '' : null" },
  templateUrl: './chart-column.html',
  styleUrl: './chart-column.css',
})
export class ChartColumn {
  readonly value = input.required<number>();
  /** The value a full bar stands for: the tallest of the chart. */
  readonly max = input.required<number>();
  readonly label = input.required<string>();
  readonly open = input(false, { transform: booleanAttribute });
  /** What an open column is, in words: read after its label, never drawn. */
  readonly openLabel = input<string>();

  protected readonly height = computed(() => barHeight(this.value(), this.max()));
}
