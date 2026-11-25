import { Component, booleanAttribute, computed, inject, input } from '@angular/core';
import { PillGroup } from '../pill-group/pill-group';

/**
 * `block` is a block of its own, drawn in the inverse colours when pressed: a wish, one
 * position of a segmented choice. `line` is one filter of a row of them above the list it
 * narrows: words, and a line under the one that is on. A row of these weighs no more than a
 * line of text, which leaves the eye to the list; the row and its rule are the screen's layout.
 *
 * In a `segmented` pill group a pill is one position of the group's track, whatever its
 * variant: the group says so once, and the position that is on fills its part of the track.
 */
export type PillVariant = 'block' | 'line';

/**
 * A pill: a filter, a wish, or one position of a segmented choice. It is a toggle: its
 * state is said with `aria-pressed`, and drawn as its variant draws it.
 */
@Component({
  selector: 'button[rsPill]',
  host: {
    type: 'button',
    '[attr.aria-pressed]': 'pressed()',
    '[attr.data-variant]': 'drawn()',
  },
  template: `<ng-content />`,
  styleUrl: './pill.css',
})
export class Pill {
  readonly pressed = input(false, { transform: booleanAttribute });
  readonly variant = input<PillVariant>('block');

  private readonly group = inject(PillGroup, { optional: true });
  /** How it is drawn: as a position of its group's track when the group has one. */
  protected readonly drawn = computed(() => (this.group?.segmented() ? 'segment' : this.variant()));
}
