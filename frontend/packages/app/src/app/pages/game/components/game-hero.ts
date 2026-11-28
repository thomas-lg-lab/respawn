import { Component, computed, input, linkedSignal, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideArrowUpRight, LucidePlay } from '@lucide/angular';
import { type Game, coverAddress } from '@respawn/data';
import { Button, Cover } from '@respawn/ui';
import { formatAgo, formatMoment } from '@app/format/format';
import {
  coverHue,
  gameTags,
  igdbPage,
  isMissing,
  steamStoreAddress,
  titleInitial,
} from '@app/games/game-list';
import { steamLaunchLink } from '@app/games/steam-launch';
import { CoverChoice } from './cover-choice';
import { GameTags } from './game-tags';

/** How many genres and themes the hero shows at first; its last pill shows the rest in place. */
export const HERO_TAGS = 4;

/**
 * Where the game stands for its owner, in one fact under its title: its last session, never
 * launched, no longer listed by its store, taken out of the library, or not in it at all.
 */
type Status =
  | { readonly kind: 'last'; readonly ago: string; readonly moment: string }
  | { readonly kind: 'never' | 'missing' | 'removed' | 'notOwned' | 'unknown' };

/**
 * The head of a game's page: its cover, which chooses among the game's covers when it has more
 * than one, its title and where it stands, the way to its page on the store and, on a
 * computer, to launch it in Steam. A press of "launch" asks the Steam
 * client of the machine it is pressed on, so a device whose only pointer is a finger does
 * not offer it, whatever its width.
 */
@Component({
  selector: 'rs-game-hero',
  imports: [
    TranslocoDirective,
    LucideArrowUpRight,
    LucidePlay,
    Button,
    Cover,
    CoverChoice,
    GameTags,
  ],
  templateUrl: './game-hero.html',
})
export class GameHero {
  readonly game = input.required<Game>();
  readonly language = input.required<string>();
  /** The moment "ago" is counted from. */
  readonly now = input.required<number>();
  /** While a choice of cover is on its way. */
  readonly choosing = input(false);

  /** A cover was chosen, by its source; null for the game's own. */
  readonly coverPicked = output<string | null>();
  /** The covers were opened, to choose again. */
  readonly coverOpened = output<void>();
  /** The game's genres, then its themes, as the game database names them. */
  protected readonly tags = computed(() => gameTags(this.game().game_data));
  /**
   * Whether the genres and themes past the first few are shown, as the last pill asked: folded
   * again for another game, the page being kept from one game to the next.
   */
  protected readonly allTags = linkedSignal({
    source: () => this.game().game_id,
    computation: () => false,
  });
  protected readonly shownTags = computed(() =>
    this.allTags() ? this.tags() : this.tags().slice(0, HERO_TAGS),
  );
  protected readonly moreTags = computed(() => Math.max(0, this.tags().length - HERO_TAGS));

  protected readonly hue = computed(() => coverHue(this.game().game_id));
  protected readonly cover = computed(() =>
    coverAddress(this.game().game_id, this.game().covers.chosen),
  );
  /** More than one source has a cover for the game: the account may choose. */
  protected readonly choosable = computed(() => this.game().covers.sources.length > 1);
  /** The cover could not be shown: the game has none, or its source did not answer. */
  protected readonly noCover = linkedSignal({ source: this.cover, computation: () => false });

  /** A game no source has named shows a question mark. */
  protected readonly initial = computed(
    () => titleInitial(this.game().name, this.language()) || '?',
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

  /** The link that launches the game in the Steam client, where a copy of it is listed. */
  protected readonly launch = steamLaunchLink(this.game);

  protected readonly store = computed(() => steamStoreAddress(this.game().stores));

  /** The game's page on IGDB, when IGDB knows the game: only an address on IGDB's own site. */
  protected readonly gameData = computed(() => igdbPage(this.game().game_data?.url));
}
