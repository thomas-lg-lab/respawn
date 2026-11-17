import { Component, input } from '@angular/core';
import { Cover, Logo } from '@respawn/ui';

/**
 * The panel beside the sign-in: the logo, a handful of covers and the tagline. Its ground
 * is dark in both themes, so it is a `dark` subtree: everything inside takes the dark
 * values of the tokens.
 */
@Component({
  selector: 'rs-sign-in-art',
  imports: [Cover, Logo],
  host: {
    'data-theme': 'dark',
    class:
      'flex min-h-75 flex-col justify-between gap-6 overflow-hidden bg-bg p-6 text-text wide:p-12',
  },
  template: `
    <rs-logo class="w-48" />
    <div class="grid -rotate-4 scale-108 grid-cols-4 gap-3" aria-hidden="true">
      @for (hue of hues; track hue) {
        <!-- One row is enough on a phone. -->
        <div [class]="$index < 4 ? '' : 'hidden wide:block'"><rs-cover [hue]="hue" /></div>
      }
    </div>
    <p class="max-w-[14em] type-page-title">{{ tagline() }}</p>
  `,
})
export class SignInArt {
  readonly tagline = input.required<string>();

  // Stand-ins for covers: no game is known before sign-in.
  protected readonly hues = [42, 268, 14, 190, 135, 215, 300, 24];
}
