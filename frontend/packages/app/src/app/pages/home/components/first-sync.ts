import { Component } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Label, Skeleton } from '@respawn/ui';

/**
 * An account is linked and its library is being read for the first time: what is on its way,
 * drawn where it will stand.
 */
@Component({
  selector: 'rs-first-sync',
  imports: [TranslocoDirective, Label, Skeleton],
  templateUrl: './first-sync.html',
})
export class FirstSync {
  protected readonly tiles = [0, 1, 2];
}
