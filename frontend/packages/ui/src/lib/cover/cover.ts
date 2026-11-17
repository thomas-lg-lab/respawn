import { Component, input } from '@angular/core';

/**
 * A cover, 2:3. For now only its placeholder: a flat block in a colour made from a hue,
 * which stands in for a game's artwork until it loads or where there is none. The hue is
 * the caller's (a game will derive its own); the saturation and the lightness are tokens.
 */
@Component({
  selector: 'rs-cover',
  host: {
    class: 'block aspect-2/3 rounded-field bg-cover',
    '[style.--rs-cover-hue]': 'hue()',
  },
  template: ``,
})
export class Cover {
  /** 0 to 360, on the colour wheel. */
  readonly hue = input.required<number>();
}
