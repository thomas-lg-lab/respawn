import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { type SimilarGame, coverAddress } from '@respawn/data';
import { Cover } from '@respawn/ui';
import { type SaidPlaytime, formatAgo, saidPlaytime } from '@app/format/format';
import { type Origin, cameThroughGame, gamePage } from '@app/games/game-page';
import { coverHue } from '@app/games/game-list';

/** A game as its row says it: its time and last session, or how long it would take. */
interface Shown {
  readonly id: string;
  readonly name: string | null;
  readonly hue: number;
  readonly cover: string;
  /** Its time in all, when a store says some was played. */
  readonly total: SaidPlaytime | null;
  /** How long ago it was last played, when a store says. */
  readonly ago: string | null;
  /** No time and no last session said by a store: never launched. Not when no store said. */
  readonly never: boolean;
  /** Never launched: how long it takes at a normal pace, when players said. */
  readonly takes: SaidPlaytime | null;
}

/**
 * The games of the owner's library the game database lists as close to this one, in its
 * order: each with what the owner did of it, or what it would ask of them.
 */
@Component({
  selector: 'rs-game-similar',
  imports: [RouterLink, TranslocoDirective, Cover],
  templateUrl: './game-similar.html',
})
export class GameSimilar {
  /** A game leads to its own page, which leads back where this one does. */
  protected readonly gamePage = gamePage;
  protected readonly from = computed(() => cameThroughGame(this.origin()));

  readonly games = input.required<readonly SimilarGame[]>();
  readonly language = input.required<string>();
  /** The moment "ago" is counted from. */
  readonly now = input.required<number>();
  /** The screen this page leads back to. */
  readonly origin = input.required<Origin>();

  protected readonly shown = computed(() => {
    const language = this.language();
    return this.games().map((game): Shown => {
      const played = game.playtime_seconds;
      const takes = game.normally_seconds;
      // Launched once a store says some time or a last session, as the server counts it; a
      // playtime no store gave is not none.
      const never = played === 0 && !game.last_played_at;
      return {
        id: game.game_id,
        name: game.name,
        hue: coverHue(game.game_id),
        cover: coverAddress(game.game_id, game.cover_source),
        total: played !== null && played > 0 ? saidPlaytime(played, language) : null,
        ago: game.last_played_at ? formatAgo(game.last_played_at, this.now(), language) : null,
        never,
        takes: never && takes !== null && takes > 0 ? saidPlaytime(takes, language) : null,
      };
    });
  });
}
