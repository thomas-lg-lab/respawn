import { Component } from '@angular/core';

/**
 * A tab of the main navigation, on the link itself: `<a rsTab routerLink="…">`. Which one is
 * the current screen is the router's to say, with `aria-current="page"`; the current one is
 * drawn in the inverse colours. The row or the bar the tabs stand in is the screen's layout.
 */
@Component({
  selector: 'a[rsTab]',
  template: `<ng-content />`,
  styleUrl: './tab.css',
})
export class Tab {}
