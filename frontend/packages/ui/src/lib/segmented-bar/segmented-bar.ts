import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * How many of `segments` are filled for `value` out of `max`. Anything above zero fills
 * at least one segment, so "a little" never reads as "nothing".
 */
export function filledSegments(value: number, max: number, segments: number): number {
  if (!(value > 0) || !(max > 0) || !(segments > 0)) return 0;
  return Math.min(segments, Math.max(1, Math.round((value / max) * segments)));
}

/**
 * A segmented bar: square segments, filled up to a value. It is a drawing, hidden from
 * screen readers, so it always comes with its value in text: project that text, and the
 * component puts it beside the bar.
 */
@Component({
  selector: 'rs-segmented-bar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex items-center gap-3.5' },
  template: `
    <span class="flex min-w-0 flex-1 gap-segment-gap" aria-hidden="true">
      @for (filled of cells(); track $index) {
        <span class="h-segment flex-1" [class]="filled ? 'bg-accent-text' : 'bg-line'"></span>
      }
    </span>
    <span class="font-semibold tabular-nums"><ng-content /></span>
  `,
})
export class SegmentedBar {
  readonly value = input.required<number>();
  readonly max = input.required<number>();
  readonly segments = input(20);

  protected readonly cells = computed(() => {
    const segments = Math.max(0, Math.floor(this.segments()));
    const filled = filledSegments(this.value(), this.max(), segments);
    return Array.from({ length: segments }, (_, index) => index < filled);
  });
}
