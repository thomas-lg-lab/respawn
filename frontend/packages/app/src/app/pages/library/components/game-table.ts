import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { type LibraryGame, coverAddress } from '@respawn/data';
import { Cover, SegmentedBar } from '@respawn/ui';
import { formatAgo, formatCount, formatMoment, playtime } from '@app/format/format';
import { cameFrom, gamePage } from '@app/games/game-page';
import { coverHue, isMissing } from '@app/games/game-list';

/** A duration as a cell shows it: a translation key and the count that goes in it. */
interface Said {
  readonly key: 'hours' | 'minutes';
  readonly count: string;
}

/** A game as its row shows it. */
interface Row {
  readonly id: string;
  readonly rank: string;
  /** `null` while no source has named the game. */
  readonly name: string | null;
  readonly hue: number;
  readonly cover: string;
  readonly missing: boolean;
  /** The total, for the bar; `null` when no store said it. */
  readonly seconds: number | null;
  readonly total: Said | null;
  /** The last two weeks: `null` when the store did not say, `'none'` for no time at all. */
  readonly recent: Said | 'none' | null;
  /**
   * The last session: how long ago and the moment itself, or the key of why there is none to
   * tell, `never` for a game never launched, `unknown` when the store did not say.
   */
  readonly last:
    | { readonly key: null; readonly ago: string; readonly moment: string }
    | { readonly key: 'never' | 'unknown'; readonly ago: null; readonly moment: null };
}

/**
 * The games as a table, one row each: its rank in the order shown, its title, its playtime
 * with a bar against the most played game, the last two weeks and the last session. What a
 * store did not say is said unknown, never shown as zero. On a phone the rank, the bar and
 * the two weeks give their room to the title, and the last session stands under it.
 */
@Component({
  selector: 'rs-game-table',
  imports: [RouterLink, TranslocoDirective, Cover, SegmentedBar],
  templateUrl: './game-table.html',
})
export class GameTable {
  /** A game leads to its page, which leads back here. */
  protected readonly gamePage = gamePage;
  protected readonly from = cameFrom('library');

  /** The games to show, in their order. */
  readonly games = input.required<readonly LibraryGame[]>();
  /** The playtime a full bar stands for. */
  readonly scale = input.required<number>();
  readonly language = input.required<string>();
  /** The moment "ago" is counted from. */
  readonly now = input.required<number>();

  protected readonly rows = computed(() =>
    this.games().map((game, index): Row => {
      const total = game.playtime_seconds;
      const recent = game.recent_playtime_seconds;
      const last = game.last_played_at;
      return {
        id: game.game_id,
        rank: String(index + 1).padStart(2, '0'),
        name: game.name,
        hue: coverHue(game.game_id),
        cover: coverAddress(game.game_id),
        missing: isMissing(game),
        seconds: total,
        total: total === null ? null : this.duration(total),
        recent: recent === null ? null : recent > 0 ? this.duration(recent) : 'none',
        last: last
          ? {
              key: null,
              ago: formatAgo(last, this.now(), this.language()),
              moment: formatMoment(last, this.language()),
            }
          : { key: total === 0 ? 'never' : 'unknown', ago: null, moment: null },
      };
    }),
  );

  private duration(seconds: number): Said {
    const { unit, count } = playtime(seconds);
    return { key: unit, count: formatCount(count, this.language()) };
  }
}
