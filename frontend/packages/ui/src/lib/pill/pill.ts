import { Component, booleanAttribute, input } from '@angular/core';

/**
 * A pill: a filter, a wish, or one position of a segmented choice. It is a toggle: its
 * state is said with `aria-pressed`, and drawn in the inverse colours when pressed.
 */
@Component({
  selector: 'button[rsPill]',
  host: {
    type: 'button',
    '[attr.aria-pressed]': 'pressed()',
  },
  template: `<ng-content />`,
  styleUrl: './pill.css',
})
export class Pill {
  readonly pressed = input(false, { transform: booleanAttribute });
}
