import { Component, booleanAttribute, input } from '@angular/core';

/**
 * A choice in a list, on the native element, so the list, the keyboard and a phone's own
 * picker stay the browser's: `<select rsSelect>`. Drawn as an outlined button with the
 * list's arrow, or, `quiet`, as its words and the arrow alone, where a box beside the others
 * would weigh too much. Its label is the screen's, a `<label>` around or beside it.
 */
@Component({
  selector: 'select[rsSelect]',
  host: { '[attr.data-quiet]': "quiet() ? '' : null" },
  template: `<ng-content />`,
  styleUrl: './select.css',
})
export class Select {
  readonly quiet = input(false, { transform: booleanAttribute });
}
