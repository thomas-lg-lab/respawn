import { Component, booleanAttribute, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Button, Label, Skeleton } from '@respawn/ui';

/**
 * An account is linked and its library is being read for the first time: what is on its way,
 * drawn where it will stand. When that reading is `late` (awaited long enough, and nothing
 * came of it), the screen stops drawing a wait and offers to ask for it.
 */
@Component({
  selector: 'rs-first-sync',
  imports: [TranslocoDirective, Button, Label, Skeleton],
  templateUrl: './first-sync.html',
})
export class FirstSync {
  readonly late = input(false, { transform: booleanAttribute });
  readonly syncNow = output();

  protected readonly tiles = [0, 1, 2];
}
