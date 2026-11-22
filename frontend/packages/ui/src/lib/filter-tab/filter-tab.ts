import { Component, booleanAttribute, input } from '@angular/core';

/**
 * One filter of a row of them, above the list it narrows: words, and a line under the one
 * that is on. It is a toggle, like a pill: its state is said with `aria-pressed`. Where a
 * pill is a block of its own, a row of these weighs no more than a line of text, which
 * leaves the eye to the list. The row and its rule are the screen's layout.
 */
@Component({
  selector: 'button[rsFilterTab]',
  host: {
    type: 'button',
    '[attr.aria-pressed]': 'pressed()',
  },
  template: `<ng-content />`,
  styleUrl: './filter-tab.css',
})
export class FilterTab {
  readonly pressed = input(false, { transform: booleanAttribute });
}
