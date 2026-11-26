import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { type LibraryGame, coverAddress } from '@respawn/data';
import { Cover } from '@respawn/ui';
import { type SaidPlaytime, saidPlaytime } from '@app/format/format';
import { cameFrom, gamePage } from '@app/games/game-page';
import { coverHue, isMissing } from '@app/games/game-list';

/** A game as its tile shows it. */
interface Tile {
  readonly id: string;
  /** `null` while no source has named the game. */
  readonly name: string | null;
  readonly hue: number;
  readonly cover: string;
  readonly missing: boolean;
  /** Its playtime in all, `never` for a game never launched, `null` when no store said. */
  readonly total: SaidPlaytime | 'never' | null;
}

/**
 * The games as covers, one tile each in the order shown: the cover, the title under it and
 * the playtime in all. Three to a row on a phone, as many as fit on a computer. A cover is
 * asked for only once its tile comes near the screen; its placeholder stands until then, and
 * where the game has none.
 */
@Component({
  selector: 'rs-game-grid',
  imports: [RouterLink, TranslocoDirective, Cover],
  templateUrl: './game-grid.html',
})
export class GameGrid {
  /** A game leads to its page, which leads back here. */
  protected readonly gamePage = gamePage;
  protected readonly from = cameFrom('library');

  /** The games to show, in their order. */
  readonly games = input.required<readonly LibraryGame[]>();
  readonly language = input.required<string>();

  protected readonly tiles = computed(() =>
    this.games().map((game): Tile => {
      const total = game.playtime_seconds;
      return {
        id: game.game_id,
        name: game.name,
        hue: coverHue(game.game_id),
        cover: coverAddress(game.game_id),
        missing: isMissing(game),
        total: total === null ? null : total === 0 ? 'never' : saidPlaytime(total, this.language()),
      };
    }),
  );
}
