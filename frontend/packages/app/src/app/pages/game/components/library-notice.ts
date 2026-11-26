import { Component, computed, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { Game } from '@respawn/data';
import { Button } from '@respawn/ui';
import { formatDate } from '@app/format/format';
import { isMissing } from '@app/games/game-list';

/**
 * Where the game stands in the library, when that asks its owner something: its store no
 * longer lists it (take it out?), it was taken out (put it back?), or it was taken out and its
 * store lists it again (put it back, or leave it out and hear no more of it). Nothing for a
 * game in the library as its store lists it.
 */
@Component({
  selector: 'rs-library-notice',
  imports: [TranslocoDirective, Button],
  templateUrl: './library-notice.html',
})
export class LibraryNotice {
  readonly game = input.required<Game>();
  readonly language = input.required<string>();
  /** It was taken out from this page a moment ago: putting it back undoes that. */
  readonly justTakenOut = input(false);
  /** An answer is awaited: nothing is asked twice. */
  readonly busy = input(false);
  readonly takeOut = output<void>();
  readonly putBack = output<void>();
  readonly dismiss = output<void>();

  protected readonly notice = computed(() => {
    const game = this.game();
    const language = this.language();
    if (game.removed?.back_since) {
      return { kind: 'back' as const, since: formatDate(game.removed.back_since, language) };
    }
    if (game.removed) return { kind: 'removed' as const };
    if (isMissing(game)) {
      // When the last copy went: the game left the library then.
      const since = game.entries
        .map((entry) => entry.missing_since ?? '')
        .sort()
        .at(-1);
      return { kind: 'missing' as const, since: since ? formatDate(since, language) : null };
    }
    return null;
  });
}
