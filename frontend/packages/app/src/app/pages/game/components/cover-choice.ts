import { Component, computed, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideCheck, LucideImage } from '@lucide/angular';
import { type GameCovers, coverAddress } from '@respawn/data';
import { Cover, Disclosure, DisclosurePanel, DisclosureTrigger } from '@respawn/ui';

/**
 * A game's cover, for an account that can choose it among its sources: the cover itself is the
 * button. Pointed at, or reached with the keyboard, it dims and says what a press does; on a
 * device whose only pointer is a finger, a small mark in its corner says it. A press opens the
 * covers side by side, the one shown marked; picking one is the choice, at once.
 */
@Component({
  selector: 'rs-cover-choice',
  imports: [
    TranslocoDirective,
    LucideCheck,
    LucideImage,
    Cover,
    Disclosure,
    DisclosurePanel,
    DisclosureTrigger,
  ],
  templateUrl: './cover-choice.html',
})
export class CoverChoice {
  readonly gameId = input.required<string>();
  readonly hue = input.required<number>();
  /** The sources the game has a cover from, the game's own first, and the one chosen. */
  readonly covers = input.required<GameCovers>();
  /** While a choice is on its way: the covers wait. */
  readonly pending = input(false);

  /** A source was picked; null for the game's own. */
  readonly picked = output<string | null>();
  /** The cover shown could not be loaded. */
  readonly failed = output<void>();

  /** The game's own cover's source: the one shown to whoever chose none. */
  protected readonly own = computed(() => this.covers().sources[0] ?? null);
  /** The source shown. */
  protected readonly shown = computed(() => this.covers().chosen ?? this.own());
  protected readonly src = computed(() => coverAddress(this.gameId(), this.covers().chosen));

  protected address(source: string): string {
    return coverAddress(this.gameId(), source === this.own() ? null : source);
  }

  protected pick(source: string, disclosure: Disclosure): void {
    if (source !== this.shown()) this.picked.emit(source === this.own() ? null : source);
    disclosure.dismiss();
  }
}
