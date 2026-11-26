import { Component, booleanAttribute, input, linkedSignal, output } from '@angular/core';

/**
 * A cover, 2:3: a game's artwork when `src` gives one, over its placeholder, a flat block in a
 * colour made from a hue, which shows while the image loads and where there is none. The hue
 * is the caller's (a game derives its own); the saturation and the lightness are tokens. An
 * image that does not load leaves the placeholder, and says so (`failed`).
 */
@Component({
  selector: 'rs-cover',
  host: { '[style.--rs-cover-hue]': 'hue()' },
  templateUrl: './cover.html',
  styleUrl: './cover.css',
})
export class Cover {
  /** 0 to 360, on the colour wheel. */
  readonly hue = input.required<number>();
  /** Where the image is; none for the placeholder alone. */
  readonly src = input<string>();
  /**
   * The image is asked for at once, for a cover in view as soon as its page opens. Without it,
   * only once the cover comes near the screen: a list of a thousand games does not fetch a
   * thousand images.
   */
  readonly eager = input(false, { transform: booleanAttribute });
  /** The image did not load: there is none to show, or it could not be read. */
  readonly failed = output<void>();

  /** The image at `src` did not load; another address is tried again. */
  protected readonly broken = linkedSignal({ source: this.src, computation: () => false });

  protected fail(): void {
    this.broken.set(true);
    this.failed.emit();
  }
}
