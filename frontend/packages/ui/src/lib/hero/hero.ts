import { Component, input } from '@angular/core';

/**
 * A hero banner: the one game a screen puts forward, on a ground made from that game's hue
 * until its artwork can stand there. The ground keeps its colour in both themes, so what
 * stands on it is always drawn as in the dark one. Its words are the screen's, projected;
 * `letter` is a decoration behind them, kept away from screen readers.
 */
@Component({
  selector: 'rs-hero',
  host: { 'data-theme': 'dark', '[style.--rs-hero-hue]': 'hue()' },
  templateUrl: './hero.html',
  styleUrl: './hero.css',
})
export class Hero {
  /** 0 to 360, on the colour wheel. */
  readonly hue = input.required<number>();
  /** One large letter behind the words, on a wide screen only. */
  readonly letter = input('');
}
