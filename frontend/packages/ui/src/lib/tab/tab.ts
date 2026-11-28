import { Component } from '@angular/core';

/**
 * A tab of the main navigation, on the link itself: `<a rsTab routerLink="…">`. Which one is
 * the current screen is the app's to say, with `aria-current="page"` (or `"true"` for the tab
 * a screen stands under without being it); the current one has the logo's cursor before its
 * words. The row or the bar the tabs stand in is the screen's layout.
 */
@Component({
  selector: 'a[rsTab]',
  template: `<ng-content />`,
  styleUrl: './tab.css',
})
export class Tab {}
