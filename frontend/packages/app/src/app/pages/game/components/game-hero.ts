import { Component, computed, inject, input, linkedSignal } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideArrowUpRight, LucidePlay } from '@lucide/angular';
import { type Game, coverAddress } from '@respawn/data';
import { Button, Cover } from '@respawn/ui';
import { formatAgo, formatMoment } from '@app/format/format';
import { coverHue, isMissing, steamLaunchAddress, steamStoreAddress } from '@app/games/game-list';
import { type Recency, recency } from '@app/games/time-map';

/**
 * Where the game stands for its owner, in one fact under its title: its last session, never
 * launched, no longer listed by its store, taken out of the library, or not in it at all.
 */
type Status =
  | { readonly kind: 'last'; readonly ago: string; readonly moment: string }
  | { readonly kind: 'never' | 'missing' | 'removed' | 'notOwned' | 'unknown' };

/**
 * The head of a game's page: its cover, its title and where it stands, the way to its page on
 * the store and, on a computer, to launch it in Steam. A press of "launch" asks the Steam
 * client of the machine it is pressed on, so a device whose only pointer is a finger does
 * not offer it, whatever its width.
 */
@Component({
  selector: 'rs-game-hero',
  imports: [TranslocoDirective, LucideArrowUpRight, LucidePlay, Button, Cover],
  templateUrl: './game-hero.html',
})
export class GameHero {
  readonly game = input.required<Game>();
  readonly language = input.required<string>();
  /** The moment "ago" is counted from. */
  readonly now = input.required<number>();

  private readonly sanitizer = inject(DomSanitizer);

  protected readonly hue = computed(() => coverHue(this.game().game_id));
  protected readonly cover = computed(() => coverAddress(this.game().game_id));
  /** The cover could not be shown: the game has none, or its source did not answer. */
  protected readonly noCover = linkedSignal({ source: this.cover, computation: () => false });

  /** The first character of the title, whole: an emoji is one, though it is two in a string. */
  protected readonly initial = computed(() =>
    ([...(this.game().name ?? '').trim()][0] ?? '?').toLocaleUpperCase(this.language()),
  );

  protected readonly status = computed((): Status => {
    const game = this.game();
    if (game.removed) return { kind: 'removed' };
    if (isMissing(game)) return { kind: 'missing' };
    if (!game.entries.length) return { kind: 'notOwned' };
    if (game.playtime_seconds === 0) return { kind: 'never' };
    if (!game.last_played_at) return { kind: 'unknown' };
    const ago = formatAgo(game.last_played_at, this.now(), this.language());
    return {
      kind: 'last',
      // It goes on after "Last session": it is not the start of the sentence.
      ago: ago.charAt(0).toLocaleLowerCase(this.language()) + ago.slice(1),
      moment: formatMoment(game.last_played_at, this.language()),
    };
  });

  /** The colour of the last session, as the time map gives it; none where there was none. */
  protected readonly recency = computed((): Recency | null => {
    const game = this.game();
    return this.status().kind === 'last' ? recency(game.last_played_at, this.now()) : null;
  });

  /**
   * The link that launches the game in the Steam client. Angular takes any address that is
   * not the web's for a dangerous one and would write it off; this one is trusted because
   * `steamLaunchAddress` only ever gives `steam://run/` followed by digits.
   */
  protected readonly launch = computed(() => {
    const address = steamLaunchAddress(this.game());
    return address === null ? null : this.sanitizer.bypassSecurityTrustUrl(address);
  });

  protected readonly store = computed(() => steamStoreAddress(this.game().stores));
}
