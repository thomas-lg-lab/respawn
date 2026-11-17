import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/**
 * The root: the screen the address names, and nothing else. It is the `app` container every
 * `wide:` style measures, so the layout follows the width the app is given, not the window's.
 */
@Component({
  selector: 'rs-root',
  imports: [RouterOutlet],
  host: { class: 'app-container block min-h-dvh' },
  template: `<router-outlet />`,
})
export class App {}
