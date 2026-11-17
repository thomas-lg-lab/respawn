import { Component } from '@angular/core';

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
  template: `
    <span class="mark">
      <svg class="icon" viewBox="0 0 48 48" aria-hidden="true">
        <rect x="7" y="14" width="7" height="24" fill="currentColor" />
        <rect x="14" y="14" width="13" height="7" fill="currentColor" />
        <rect x="20" y="21" width="7" height="6" fill="currentColor" />
        <rect class="cursor" x="31" y="14" width="10" height="24" />
      </svg>
      <span class="name"
        >{{ name }}<span class="dot">{{ dot }}</span></span
      >
    </span>
  `,
  styleUrl: './logo.css',
})
export class Logo {
  // A name, not a sentence: it is the same in every language.
  protected readonly name = 'respawn';
  protected readonly dot = '.';
}
