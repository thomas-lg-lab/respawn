import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { LibraryStats } from '@respawn/data';
import { SegmentedBar, StatTile } from '@respawn/ui';
import { formatCount, isOne, playtime } from '@app/format/format';
import { libraryWords } from '@app/games/game-list';

/** A figure as a tile shows it: a translation key and what goes in it. */
interface Said {
  readonly key: string;
  readonly count?: string;
}

/**
 * The library in figures: the time played in all, over the last two weeks, and the games
 * never launched. Each says what it covers; one the store did not give is said unknown,
 * never shown as zero. Each leads to the games it counts: the library, filtered and sorted to
 * show them.
 */
@Component({
  selector: 'rs-figures',
  imports: [RouterLink, TranslocoDirective, SegmentedBar, StatTile],
  templateUrl: './figures.html',
})
export class Figures {
  readonly stats = input.required<LibraryStats>();
  readonly language = input.required<string>();

  /** Where each figure leads: the library, showing the games it counts. */
  protected readonly launchedWords = libraryWords({ filter: 'launched', sort: 'playtime' });
  protected readonly recentWords = libraryWords({ filter: 'recent' });
  protected readonly neverWords = libraryWords({ filter: 'never' });

  protected readonly total = computed(() => this.duration(this.stats().playtime_seconds));
  protected readonly recent = computed(() => this.duration(this.stats().recent_playtime_seconds));

  protected readonly launched = computed(() => this.games(this.stats().launched));
  protected readonly recentlyPlayed = computed(() => this.games(this.stats().recently_played));
  protected readonly owned = computed(() => this.games(this.stats().games));
  /**
   * `null` while no store said any playtime: every game would count as never launched,
   * which nothing says.
   */
  protected readonly neverLaunched = computed(() =>
    this.stats().playtime_seconds === null
      ? null
      : formatCount(this.stats().never_launched, this.language()),
  );

  private duration(seconds: number | null): Said {
    if (seconds === null) return { key: 'unknown' };
    const { unit, count } = playtime(seconds);
    return { key: unit, count: formatCount(count, this.language()) };
  }

  /** One game or several is the language's to say: French counts none as one. */
  private games(count: number): Said {
    const one = isOne(count, this.language());
    return { key: one ? 'ofOne' : 'ofMany', count: formatCount(count, this.language()) };
  }
}
