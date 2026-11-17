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
  templateUrl: './logo.html',
  styleUrl: './logo.css',
})
export class Logo {
  // A name, not a sentence: it is the same in every language.
  protected readonly name = 'respawn';
  protected readonly dot = '.';
}
