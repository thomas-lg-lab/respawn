import { Component } from '@angular/core';

/**
 * A choice in a list, on the native element, so the list, the keyboard and a phone's own
 * picker stay the browser's: `<select rsSelect>`. Drawn as an outlined button with the
 * list's arrow. Its label is the screen's, a `<label>` around or beside it.
 */
@Component({
  selector: 'select[rsSelect]',
  template: `<ng-content />`,
  styleUrl: './select.css',
})
export class Select {}
