import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { cameFrom, gamePage } from '@app/games/game-page';
import { Card, SegmentedBar } from '@respawn/ui';
import { formatCount, isOne, saidPlaytime } from '@app/format/format';
import type { Ranking } from '../library-blocks';

/**
 * Where the time went: the most played games, each with a bar against the first, the
 * playtime of the whole library, and what the launched games it does not name add up to.
 */
@Component({
  selector: 'rs-time-ranking',
  imports: [RouterLink, TranslocoDirective, Card, SegmentedBar],
  templateUrl: './time-ranking.html',
})
export class TimeRanking {
  /** A game leads to its page, which leads back here. */
  protected readonly gamePage = gamePage;
  protected readonly from = cameFrom('home');

  readonly ranking = input.required<Ranking>();
  readonly language = input.required<string>();

  protected readonly total = computed(() => saidPlaytime(this.ranking().seconds, this.language()));

  /** A full bar is the first game's playtime. */
  protected readonly scale = computed(() => this.ranking().top[0]?.playtime_seconds ?? 0);

  protected readonly rows = computed(() =>
    this.ranking().top.map((game, index) => ({
      id: game.game_id,
      rank: String(index + 1).padStart(2, '0'),
      name: game.name,
      seconds: game.playtime_seconds ?? 0,
      time: saidPlaytime(game.playtime_seconds ?? 0, this.language()),
    })),
  );

  /** The other launched games; `null` when the ranking names them all. */
  protected readonly others = computed(() => {
    const { count, seconds } = this.ranking().others;
    if (count === 0) return null;
    const language = this.language();
    return {
      one: isOne(count, language),
      count: formatCount(count, language),
      time: saidPlaytime(seconds, language),
    };
  });
}
