import { Component, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Banner, Button } from '@respawn/ui';
import type { SyncNotice } from '@app/sync/sync-view';

/**
 * What the home says of the sync above the library it shows: one is running, or the last
 * one brought nothing and the games shown are older, with the way to try again. It never
 * takes the place of the games.
 */
@Component({
  selector: 'rs-sync-banner',
  imports: [TranslocoDirective, Banner, Button],
  templateUrl: './sync-banner.html',
})
export class SyncBanner {
  readonly notice = input.required<SyncNotice>();
  /** The translation key of why the last reading failed. */
  readonly reason = input<string>();
  /** When a reading last succeeded, written out. */
  readonly lastSuccess = input<string>();

  readonly syncNow = output();
  /** Only what is not a failure can be put away: `stale`. */
  readonly dismiss = output();
}
