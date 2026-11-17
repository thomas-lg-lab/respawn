import { Component, computed, input } from '@angular/core';
import { LucideCircleAlert, LucideInfo } from '@lucide/angular';

/** `status` says what is going on, `warn` what went wrong. */
export type BannerTone = 'status' | 'warn';

const TONES: Record<BannerTone, string> = {
  status: 'border-line bg-surface text-text',
  warn: 'border-warn-line bg-warn-bg text-warn',
};

/**
 * A banner: one sentence with its icon, above the content it speaks of and never in its
 * place. What it is to a screen reader is the screen's call: `role="alert"` for a refusal
 * that just happened, `role="status"` for news that can wait.
 */
@Component({
  selector: 'rs-banner',
  imports: [LucideCircleAlert, LucideInfo],
  host: { '[class]': 'classes()' },
  template: `
    @if (tone() === 'warn') {
      <svg
        lucideCircleAlert
        class="mt-px size-5 shrink-0"
        [strokeWidth]="2"
        aria-hidden="true"
      ></svg>
    } @else {
      <svg lucideInfo class="mt-px size-5 shrink-0" [strokeWidth]="2" aria-hidden="true"></svg>
    }
    <div class="min-w-0 flex-1"><ng-content /></div>
  `,
})
export class Banner {
  readonly tone = input<BannerTone>('status');

  protected readonly classes = computed(
    () =>
      `flex items-start gap-2.5 rounded-field border px-3.5 py-3 font-medium ${TONES[this.tone()]}`,
  );
}
