import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideChevronRight } from '@lucide/angular';
import type { LibraryGame } from '@respawn/data';
import { Card } from '@respawn/ui';
import { formatCount, isOne } from '@app/format/format';
import { type LibraryLength, kept, lengthOf, libraryWords } from '@app/games/game-list';

/** The lengths a duration is known for, in their order, each with its shade on the bar. */
const KNOWN = ['short', 'medium', 'long'] as const satisfies readonly LibraryLength[];
const SHADES: Record<(typeof KNOWN)[number], string> = {
  short: 'bg-recency-1',
  medium: 'bg-recency-2',
  long: 'bg-recency-3',
};

/**
 * Hours rounded as "about" says them: to ten from a thousand, to five from a hundred, whole
 * under it, and one at least, since a game known to take a little still takes some.
 */
export function aboutHours(seconds: number): number {
  const hours = seconds / 3600;
  const step = hours >= 1000 ? 10 : hours >= 100 ? 5 : 1;
  return Math.max(1, Math.round(hours / step) * step);
}

/** Whether a game never launched has a known duration: the backlog has a figure to give. */
export function hasBacklog(games: readonly LibraryGame[]): boolean {
  return kept(games, 'never').some((game) => lengthOf(game) !== 'unknown');
}

/**
 * What is left to play, in hours: the games never launched, each at the game database's
 * normal pace, split by length; a game whose duration is not known, or past what any game
 * takes to finish, is counted apart, in games, and never guessed. Until a game can be said finished or dropped, a game launched is left out:
 * its time played says neither. Each length leads to those games in the library. Not drawn
 * when no game never launched has a known duration: there is no figure to give.
 */
@Component({
  selector: 'rs-backlog',
  imports: [RouterLink, TranslocoDirective, LucideChevronRight, Card],
  templateUrl: './backlog.html',
})
export class Backlog {
  readonly games = input.required<readonly LibraryGame[]>();
  readonly language = input.required<string>();

  private readonly never = computed(() => kept(this.games(), 'never'));

  private readonly buckets = computed(() =>
    KNOWN.map((length) => {
      const games = this.never().filter((game) => lengthOf(game) === length);
      const seconds = games.reduce((sum, game) => sum + (game.normally_seconds ?? 0), 0);
      return { length, games: games.length, seconds };
    }),
  );

  private readonly seconds = computed(() =>
    this.buckets().reduce((sum, bucket) => sum + bucket.seconds, 0),
  );
  private readonly known = computed(() =>
    this.buckets().reduce((sum, bucket) => sum + bucket.games, 0),
  );

  protected readonly total = computed(() =>
    formatCount(aboutHours(this.seconds()), this.language()),
  );

  /** "across 412 of the 819 games never launched". */
  protected readonly covers = computed(() => ({
    one: this.never().length === 1,
    known: formatCount(this.known(), this.language()),
    count: formatCount(this.never().length, this.language()),
  }));

  /** One row per length that holds a game, the unknown one last, each leading to its games. */
  protected readonly rows = computed(() => {
    const language = this.language();
    const known = this.buckets()
      .filter((bucket) => bucket.games > 0)
      .map((bucket) => ({
        length: bucket.length,
        shade: SHADES[bucket.length],
        share: (bucket.seconds / this.seconds()) * 100,
        one: isOne(bucket.games, language),
        count: formatCount(bucket.games, language),
        time: formatCount(aboutHours(bucket.seconds), language),
        words: libraryWords({ filter: 'never', lengths: [bucket.length] }),
      }));
    const unknown = this.never().length - this.known();
    return {
      known,
      unknown:
        unknown > 0
          ? {
              one: isOne(unknown, language),
              count: formatCount(unknown, language),
              words: libraryWords({ filter: 'never', lengths: ['unknown'] }),
            }
          : null,
    };
  });
}
