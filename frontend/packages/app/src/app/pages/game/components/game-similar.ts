import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { type SimilarGame, coverAddress } from '@respawn/data';
import { Cover } from '@respawn/ui';
import { type SaidPlaytime, formatAgo, saidPlaytime } from '@app/format/format';
import { type Origin, cameFrom, gamePage } from '@app/games/game-page';
import { coverHue } from '@app/games/game-list';

/** A game as its row says it: its time and last session, or how long it would take. */
interface Shown {
  readonly id: string;
  readonly name: string | null;
  readonly hue: number;
  readonly cover: string;
  /** Launched: its time in all, and how long ago it was last played when a store says. */
  readonly total: SaidPlaytime | null;
  readonly ago: string | null;
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
  protected readonly from = computed(() => cameFrom(this.origin()));

  readonly games = input.required<readonly SimilarGame[]>();
  readonly language = input.required<string>();
  /** The moment "ago" is counted from. */
  readonly now = input.required<number>();
  /** The screen this page leads back to. */
  readonly origin = input.required<Origin>();

  protected readonly shown = computed(() => {
    const language = this.language();
    return this.games().map((game): Shown => {
      const played = game.playtime_seconds ?? 0;
      return {
        id: game.game_id,
        name: game.name,
        hue: coverHue(game.game_id),
        cover: coverAddress(game.game_id, game.cover_source),
        total: played > 0 ? saidPlaytime(played, language) : null,
        ago:
          played > 0 && game.last_played_at
            ? formatAgo(game.last_played_at, this.now(), language)
            : null,
        takes:
          played === 0 && (game.normally_seconds ?? 0) > 0
            ? saidPlaytime(game.normally_seconds ?? 0, language)
            : null,
      };
    });
  });
}
