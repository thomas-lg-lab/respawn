import { Component, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Banner, Button, Card } from '@respawn/ui';

/**
 * Who may create an account on the instance, as its administrator sees and decides it: open
 * to anyone who reaches it, or closed. One press says the other.
 */
@Component({
  selector: 'rs-registration',
  imports: [TranslocoDirective, Banner, Button, Card],
  templateUrl: './registration.html',
})
export class Registration {
  /** Whether anyone may register now. */
  readonly open = input.required<boolean>();
  /** While the change asked for is on its way. */
  readonly busy = input(false);
  /** Why the last change was refused, as a translation key; none when it was not. */
  readonly failure = input<string | null>(null);

  /** What the administrator asks for: open, or closed. */
  readonly changed = output<boolean>();
}
