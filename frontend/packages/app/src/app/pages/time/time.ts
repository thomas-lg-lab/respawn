import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LibraryQueries } from '@respawn/data';
import { Banner, Button, Skeleton } from '@respawn/ui';
import { injectQuery } from '@tanstack/angular-query';
import { failureKey } from '@app/errors/failure-message';
import { formatAgo, formatCount, isOne, saidPlaytime } from '@app/format/format';
import { LanguageSetting } from '@app/i18n/language';
import { TimeMap } from './components/time-map';
import { type TimeTile, timeTiles } from '@app/games/time-tiles';

const RECENCIES = [1, 2, 3, 4] as const;

/**
 * Where the time went, as a map: the most played games as tiles, each as large as its
 * playtime and coloured by its last session, the other launched games in a band under them. A tile
 * pressed is said under the map. Like the library, it shows what was read and leaves the home
 * to say how the library gets there.
 */
@Component({
  selector: 'rs-time',
  imports: [RouterLink, TranslocoDirective, Banner, Button, Skeleton, TimeMap],
  templateUrl: './time.html',
})
export class Time {
  private readonly queries = inject(LibraryQueries);
  private readonly language = inject(LanguageSetting);

  protected readonly failureKey = failureKey;
  protected readonly recencies = RECENCIES;
  protected readonly currentLanguage = this.language.current;
  protected readonly games = injectQuery(() => this.queries.games());
  /** "Ago" is counted from the moment the games were read, so it follows each new reading. */
  private readonly now = computed(() => this.games.dataUpdatedAt() || Date.now());

  protected readonly map = computed(() =>
    timeTiles(this.games.data() ?? [], this.language.current(), this.now()),
  );

  /** The tile pressed last; the most played game until one is, or once it left the map. */
  private readonly picked = signal<string | null>(null);
  protected readonly selected = computed(() => {
    const map = this.map();
    const tiles: TimeTile[] = map ? [...map.tiles, ...(map.others ? [map.others] : [])] : [];
    return tiles.find((tile) => tile.id === this.picked()) ?? tiles[0] ?? null;
  });

  protected readonly heading = computed(() => {
    const map = this.map();
    return map
      ? {
          time: saidPlaytime(map.seconds, this.language.current()),
          count: formatCount(map.launched, this.language.current()),
        }
      : null;
  });

  /** What is said of the tile pressed: its time, its share of the whole, its last session. */
  protected readonly detail = computed(() => {
    const map = this.map();
    const tile = this.selected();
    if (!map || !tile) return null;
    const language = this.language.current();
    const lastPlayedAt = tile.kind === 'game' ? tile.game.last_played_at : null;
    const ago = lastPlayedAt ? formatAgo(lastPlayedAt, this.now(), language) : null;
    return {
      tile,
      others:
        tile.kind === 'others'
          ? {
              one: isOne(tile.count, language),
              count: formatCount(tile.count, language),
            }
          : null,
      time: saidPlaytime(tile.seconds, language),
      share: share(tile.seconds / map.seconds, language),
      // The sentence goes on after "Last session": it is not its start.
      ago: ago && ago.charAt(0).toLocaleLowerCase(language) + ago.slice(1),
    };
  });

  protected pick(id: string): void {
    this.picked.set(id);
  }
}

/** A share of the whole, as a percentage: whole, unless that says nothing of a small one. */
function share(fraction: number, language: string): string {
  const whole = new Intl.NumberFormat(language, { style: 'percent', maximumFractionDigits: 0 });
  return fraction > 0 && fraction < 0.005
    ? new Intl.NumberFormat(language, { style: 'percent', maximumFractionDigits: 1 }).format(
        fraction,
      )
    : whole.format(fraction);
}
