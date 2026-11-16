import { ChangeDetectionStrategy, Component } from '@angular/core';
import { Logo } from '@respawn/ui';
import { Foundations } from './pages/foundations/foundations';

/**
 * The shell: the header and, for now, the one screen. It is the `app` container every
 * `wide:` style measures, so the layout follows the width the app is given, not the window's.
 */
@Component({
  selector: 'rs-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Logo, Foundations],
  host: { class: 'app-container block min-h-dvh' },
  templateUrl: './app.html',
})
export class App {}
