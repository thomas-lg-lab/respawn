import { Component, computed, inject } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

/**
 * The root: the screen the address names, and nothing else. It is the `app` container every
 * `wide:` style measures, so the layout follows the width the app is given, not the window's.
 *
 * Until the first screen is there, it says that the app is on its way. Getting to a screen
 * can take a while: a guard asks who is signed in, and the API may be slow to say (its
 * database away). Without a word the page would stay empty for as long.
 */
@Component({
  selector: 'rs-root',
  imports: [RouterOutlet, TranslocoDirective],
  host: { class: 'app-container block min-h-dvh' },
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly router = inject(Router);

  /** No navigation has ended yet: no screen is drawn. */
  protected readonly starting = computed(() => this.router.lastSuccessfulNavigation() === null);
}
