import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LIBRARY_GENRE } from '@app/games/game-list';

/**
 * A game's genres and themes, as the game database names them (English), each a link to the
 * library narrowed to the games that bear it. The list's name is an input: it is the page's
 * words.
 */
@Component({
  selector: 'rs-game-tags',
  imports: [RouterLink],
  templateUrl: './game-tags.html',
})
export class GameTags {
  readonly tags = input.required<readonly string[]>();
  /** What the list is, for those who hear it. */
  readonly label = input.required<string>();

  /** The library's address narrowed to one of them. */
  protected words(tag: string): Record<string, string> {
    return { [LIBRARY_GENRE]: tag };
  }
}
