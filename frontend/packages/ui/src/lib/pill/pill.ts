import { ChangeDetectionStrategy, Component, booleanAttribute, input } from '@angular/core';

/**
 * A pill: a filter, a wish, or one position of a segmented choice. It is a toggle: its
 * state is said with `aria-pressed`, and drawn in the inverse colours when pressed.
 */
@Component({
  selector: 'button[rsPill]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    type: 'button',
    '[attr.aria-pressed]': 'pressed()',
    class:
      'inline-flex min-h-touch shrink-0 items-center justify-center rounded-pill border border-control-line ' +
      'bg-transparent px-4 type-control font-medium text-text ' +
      'transition-[background-color,color,border-color,translate] duration-hover ' +
      'hover:bg-surface-2 active:translate-y-px ' +
      'aria-pressed:border-inverse aria-pressed:bg-inverse aria-pressed:text-on-inverse ' +
      'disabled:pointer-events-none disabled:opacity-50',
  },
  template: `<ng-content />`,
})
export class Pill {
  readonly pressed = input(false, { transform: booleanAttribute });
}
