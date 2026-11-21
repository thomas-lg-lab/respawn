import { Component, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideTriangleAlert } from '@lucide/angular';
import { Button } from '@respawn/ui';

/**
 * The first reading of the library ended and there is no game to show: it `failed`, and
 * `reason` is the key of the sentence that says why, or the store showed `nothing-visible`,
 * which is most often a private profile, and the steps to open it are given.
 */
@Component({
  selector: 'rs-sync-issue',
  imports: [TranslocoDirective, Button, LucideTriangleAlert],
  templateUrl: './sync-issue.html',
})
export class SyncIssue {
  readonly kind = input.required<'failed' | 'nothing-visible'>();
  /** The translation key of why the reading failed. */
  readonly reason = input<string>();
  readonly retry = output();

  protected readonly steps = ['profile', 'privacy', 'back'];
}
