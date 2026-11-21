import { Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideLink } from '@lucide/angular';
import { Button, Label } from '@respawn/ui';

/**
 * Nothing is linked yet: what linking a Steam account brings, and the way to it. The link is
 * a navigation to `address`, which goes through Steam and comes back.
 */
@Component({
  selector: 'rs-no-account',
  imports: [TranslocoDirective, Button, Label, LucideLink],
  templateUrl: './no-account.html',
})
export class NoAccount {
  readonly address = input.required<string>();
}
