import { Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideArrowUpRight } from '@lucide/angular';
import { GameTags } from './game-tags';

/**
 * What the game database says the game is: its summary and every genre and theme it gives,
 * in its own words (IGDB: English, so marked as such), when it came out, and where it all
 * comes from. The hero
 * shows the first few genres; this card holds them all.
 */
@Component({
  selector: 'rs-game-about',
  imports: [TranslocoDirective, LucideArrowUpRight, GameTags],
  templateUrl: './game-about.html',
})
export class GameAbout {
  readonly summary = input.required<string | null>();
  readonly tags = input.required<readonly string[]>();
  /** The game's page in the game database; none when it cannot be followed. */
  readonly url = input.required<string | null>();
  /** The month and year it came out, written in the reader's language; none when not said. */
  readonly released = input<string | null>(null);
}
