import { ChangeDetectionStrategy, Component } from '@angular/core';

/**
 * The logo: the cursor icon (a pixel "r" with its hook, then a block in the accent) and the
 * name, whose final dot takes the accent too. The name is always in the logo face, whatever
 * the character.
 *
 * The host is a container: under 120px of width only the icon is drawn, and the name stays
 * there for a screen reader. So give the host a width (it is a block), or it has none to
 * measure.
 */
@Component({
  selector: 'rs-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block [container-type:inline-size]' },
  template: `
    <span class="flex items-center gap-2 text-text">
      <svg class="size-8 shrink-0" viewBox="0 0 48 48" aria-hidden="true">
        <rect x="7" y="14" width="7" height="24" fill="currentColor" />
        <rect x="14" y="14" width="13" height="7" fill="currentColor" />
        <rect x="20" y="21" width="7" height="6" fill="currentColor" />
        <rect class="fill-accent-text" x="31" y="14" width="10" height="24" />
      </svg>
      <span class="font-logo text-[1.5rem] leading-none font-bold @max-[119px]:sr-only"
        >{{ name }}<span class="text-accent-text">{{ dot }}</span></span
      >
    </span>
  `,
})
export class Logo {
  // A name, not a sentence: it is the same in every language.
  protected readonly name = 'respawn';
  protected readonly dot = '.';
}
