import { Component, input } from '@angular/core';
import { LucideCircleAlert, LucideInfo } from '@lucide/angular';

/** `status` says what is going on, `warn` what went wrong. */
export type BannerTone = 'status' | 'warn';

/**
 * A banner: one sentence with its icon, above the content it speaks of and never in its
 * place. What it is to a screen reader is the screen's call: `role="alert"` for a refusal
 * that just happened, `role="status"` for news that can wait.
 */
@Component({
  selector: 'rs-banner',
  imports: [LucideCircleAlert, LucideInfo],
  host: { '[attr.data-tone]': 'tone()' },
  template: `
    @if (tone() === 'warn') {
      <svg lucideCircleAlert class="icon" [strokeWidth]="2" aria-hidden="true"></svg>
    } @else {
      <svg lucideInfo class="icon" [strokeWidth]="2" aria-hidden="true"></svg>
    }
    <div class="content"><ng-content /></div>
  `,
  styleUrl: './banner.css',
})
export class Banner {
  readonly tone = input<BannerTone>('status');
}
