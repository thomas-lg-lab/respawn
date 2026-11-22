import { Component, computed, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { LibraryGame } from '@respawn/data';
import { Button, Card, Cover } from '@respawn/ui';
import { coverHue } from '@app/games/game-list';

/**
 * A few games never launched, drawn at random by the screen, and the way to ask for another
 * draw while there are other games to draw.
 */
@Component({
  selector: 'rs-random-picks',
  imports: [TranslocoDirective, Button, Card, Cover],
  templateUrl: './random-picks.html',
})
export class RandomPicks {
  readonly games = input.required<readonly LibraryGame[]>();
  /** Whether another draw could give other games. */
  readonly more = input(false);
  readonly reroll = output<void>();

  protected readonly picks = computed(() =>
    this.games().map((game) => ({
      id: game.game_id,
      name: game.name,
      hue: coverHue(game.game_id),
    })),
  );
}
