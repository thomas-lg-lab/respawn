import { Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Cover } from '@respawn/ui';
import { type SaidPlaytime, formatAgo, saidPlaytime } from '@app/format/format';
import { coverHue } from '@app/games/game-list';
import type { PlayedRecently as Played } from '../library-blocks';

/** A game as its card shows it: what is said first, then its playtime in all. */
interface Shown {
  readonly id: string;
  readonly name: string | null;
  readonly hue: number;
  /** The last two weeks, when that is what the games were chosen by. */
  readonly recent: SaidPlaytime | null;
  /** How long ago the last session was, otherwise. */
  readonly ago: string | null;
  readonly total: SaidPlaytime | null;
}

/**
 * The games played lately, beside the one the banner resumes: those of the last two weeks
 * with their time over them, or the latest sessions when the store names none.
 */
@Component({
  selector: 'rs-played-recently',
  imports: [TranslocoDirective, Cover],
  templateUrl: './played-recently.html',
})
export class PlayedRecently {
  readonly played = input.required<Played>();
  readonly language = input.required<string>();
  /** The moment "ago" is counted from. */
  readonly now = input.required<number>();

  protected readonly games = computed(() => {
    const { by, games } = this.played();
    const language = this.language();
    return games.map((game): Shown => {
      const recent = by === 'recent' ? game.recent_playtime_seconds : null;
      const last = by === 'last' ? game.last_played_at : null;
      const total = game.playtime_seconds;
      return {
        id: game.game_id,
        name: game.name,
        hue: coverHue(game.game_id),
        recent: recent === null ? null : saidPlaytime(recent, language),
        ago: last === null ? null : formatAgo(last, this.now(), language),
        total: total === null ? null : saidPlaytime(total, language),
      };
    });
  });
}
