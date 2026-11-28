import { Location } from '@angular/common';
import { Component, computed, inject, linkedSignal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideChevronLeft } from '@lucide/angular';
import { ApiFailure, GameQueries, LibraryQueries } from '@respawn/data';
import { Banner, Button, Skeleton, StatTile } from '@respawn/ui';
import { injectMutation, injectQuery } from '@tanstack/angular-query';
import { failureKey } from '@app/errors/failure-message';
import {
  formatCount,
  formatDate,
  formatMonthOf,
  formatShare,
  ordinalForm,
  saidPlaytime,
} from '@app/format/format';
import { gameTags, igdbPage } from '@app/games/game-list';
import { ORIGINS, type Origin, originIn } from '@app/games/game-page';
import { LanguageSetting } from '@app/i18n/language';
import { GameAbout } from './components/game-about';
import { GameDurations } from './components/game-durations';
import { GameHero } from './components/game-hero';
import { GameSimilar } from './components/game-similar';
import { LibraryNotice } from './components/library-notice';
import { MonthCalendar, monthName } from './components/month-calendar';
import { MonthDetail } from './components/month-detail';
import { type MonthKey, calendar, monthDetail, secondsIn } from './game-view';

/**
 * The page of one game, as its owner sees it: where it stands, its figures, what was played of
 * it month by month and, for a month pressed, what else was played then; the way to launch it
 * in Steam; the cover it shows, among its sources'; and, for a game its store no longer lists,
 * taking it out of the library or putting it back. Reached from the library, the home, the time map and the search, it leads back to
 * where it was reached from.
 */
@Component({
  selector: 'rs-game',
  imports: [
    TranslocoDirective,
    LucideChevronLeft,
    Banner,
    Button,
    Skeleton,
    StatTile,
    GameAbout,
    GameDurations,
    GameHero,
    GameSimilar,
    LibraryNotice,
    MonthCalendar,
    MonthDetail,
  ],
  templateUrl: './game.html',
})
export class Game {
  private readonly queries = inject(GameQueries);
  private readonly library = inject(LibraryQueries);
  private readonly location = inject(Location);
  private readonly router = inject(Router);
  private readonly language = inject(LanguageSetting);

  protected readonly failureKey = failureKey;
  protected readonly igdbPage = igdbPage;
  protected readonly currentLanguage = this.language.current;

  protected readonly id = toSignal(
    inject(ActivatedRoute).paramMap.pipe(map((params) => params.get('id') ?? '')),
    { requireSync: true },
  );

  /**
   * The screen the page was reached from, as the link said it: the back link names it. A page
   * opened from its address alone leads back to the library. Read again at each navigation:
   * another game opened from this page (its search) is the same page, and says its own.
   */
  private readonly cameFrom = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => originIn(this.location.getState())),
      startWith(originIn(this.location.getState())),
    ),
    { requireSync: true },
  );
  protected readonly origin = computed((): Origin => this.cameFrom() ?? 'library');
  /** Where the back link leads, as an address the browser can also open on its own. */
  protected readonly back = computed(() =>
    this.location.prepareExternalUrl(ORIGINS[this.origin()]),
  );

  protected readonly game = injectQuery(() => this.queries.game(this.id()));
  protected readonly months = injectQuery(() => this.queries.months(this.id()));
  /** The library's years say which months were counted at all, and the time of this one. */
  protected readonly years = injectQuery(() => this.library.years());
  protected readonly stats = injectQuery(() => this.library.stats());

  protected readonly takingOut = injectMutation(() => this.queries.takeOut());
  protected readonly puttingBack = injectMutation(() => this.queries.putBack());
  protected readonly dismissing = injectMutation(() => this.queries.dismissNotice());
  protected readonly choosing = injectMutation(() => this.queries.chooseCover());

  /** A choice of cover for the game shown: on its way, or refused. */
  protected readonly choosingCover = computed(
    () => this.choosing.isPending() && this.choosing.variables()?.id === this.id(),
  );
  protected readonly coverRefused = computed(
    () => this.choosing.isError() && this.choosing.variables()?.id === this.id(),
  );

  /** "Ago" is counted from the moment the game was read, so it follows each new reading. */
  protected readonly now = computed(() => this.game.dataUpdatedAt() || Date.now());

  /** The game does not exist: an address made by hand, or a game the catalogue forgot. */
  protected readonly unknown = computed(() => {
    const error = this.game.error();
    return error instanceof ApiFailure && error.status === 404;
  });

  protected readonly calendar = computed(() => {
    const game = this.game.data();
    const months = this.months.data();
    // The library's years say which months were counted: without them, an empty month could
    // not be told from one nothing counted.
    const years = this.years.data();
    if (!game || !months || !years) return null;
    return calendar(months.months, years.years, game.playtime_seconds, this.now());
  });

  /** The month pressed; the last month played until one is, and again on another game. */
  protected readonly picked = linkedSignal<string, MonthKey | null>({
    source: this.id,
    computation: () => null,
  });
  protected readonly selected = computed(() => this.picked() ?? this.calendar()?.last ?? null);

  protected readonly month = injectQuery(() => ({
    ...this.queries.month(this.selected() ?? ''),
    enabled: this.selected() !== null,
  }));

  /** The game's own time in the month pressed, from its months. */
  protected readonly selectedSeconds = computed(() => {
    const key = this.selected();
    return (this.months.data()?.months ?? [])
      .filter((month) => month.month.startsWith(`${key}-`))
      .reduce((sum, month) => sum + month.seconds, 0);
  });

  protected readonly detail = computed(() => {
    const month = this.month.data();
    return month ? monthDetail(month, this.id(), this.selectedSeconds()) : null;
  });

  /**
   * The four figures: the time in all and its rank, this year and its share of the year, the
   * last two weeks and their share, the best month. What is not known is a dash, never a zero.
   */
  protected readonly figures = computed(() => {
    const game = this.game.data();
    if (!game) return null;
    const language = this.language.current();
    const time = (seconds: number | null) =>
      seconds === null ? null : saidPlaytime(seconds, language);
    const share = (part: number, whole: number | null | undefined) =>
      whole ? formatShare(part / whole, language) : null;

    const year = new Date(this.now()).getUTCFullYear();
    const months = this.months.data()?.months;
    // The library's year says what of it was counted: nothing counted is not known, and a year
    // counted from a later day says from when.
    const counted = this.years.data()?.years.find((each) => each.year === year);
    const thisYear = months && counted ? secondsIn(months, year) : null;
    const recent = game.recent_playtime_seconds;
    const recentTotal = this.stats.data()?.recent_playtime_seconds;
    const best = this.calendar()?.best ?? null;
    // Once the history says there is no best month, the month it was last played instead.
    const last =
      !best && this.calendar() && game.last_played_at
        ? formatMonthOf(game.last_played_at.slice(0, 10), language)
        : null;

    return {
      total: time(game.playtime_seconds),
      rank: game.rank
        ? {
            form: ordinalForm(game.rank.position, language),
            position: formatCount(game.rank.position, language),
            launched: formatCount(game.rank.launched, language),
          }
        : null,
      year: time(thisYear),
      yearShare:
        thisYear !== null && counted?.whole && counted.seconds
          ? { share: share(thisYear, counted.seconds), whole: time(counted.seconds)! }
          : null,
      yearSince:
        thisYear !== null && counted && !counted.whole
          ? formatDate(counted.first_day, language)
          : null,
      recent: time(recent),
      recentShare:
        recent !== null && recentTotal
          ? { share: share(recent, recentTotal), whole: time(recentTotal)! }
          : null,
      best: best ? { time: time(best.seconds)!, month: monthName(best.month, language) } : null,
      last: last ? last.charAt(0).toLocaleUpperCase(language) + last.slice(1) : null,
    };
  });

  /** Every genre and theme the game database gives the game: the hero shows the first ones. */
  protected readonly tags = computed(() => gameTags(this.game.data()?.game_data));

  /**
   * A game last played before this year has nothing to say of this year or these two weeks: read
   * from the game alone, so the figures do not change once the history comes.
   */
  protected readonly quiet = computed(() => {
    const last = this.game.data()?.last_played_at;
    return !!last && Date.parse(last) < Date.UTC(new Date(this.now()).getUTCFullYear(), 0, 1);
  });

  /** The month the game came out, in the reader's language; none while the database says none. */
  protected readonly released = computed(() => {
    const day = this.game.data()?.game_data?.released_on;
    return day ? formatMonthOf(day, this.language.current()) : null;
  });

  /** The games of the library the game database lists as close to this one. */
  protected readonly similar = computed(() => this.game.data()?.game_data?.similar ?? []);

  /**
   * Whether the history has a place: not for a game never launched, nor once the history says
   * no month of it is known. While it comes, its place waits for it.
   */
  protected readonly historyShown = computed(
    () => !this.neverLaunched() && this.calendar()?.rows.length !== 0,
  );

  /** Whether "About" has anything to say: a summary, genres, or the day it came out. */
  protected readonly aboutShown = computed(() => {
    const said = this.game.data()?.game_data;
    return !!said && (!!said.summary || this.tags().length > 0 || !!said.released_on);
  });

  /** Whether anything stands under the figures. */
  protected readonly body = computed(
    () => this.historyShown() || this.aboutShown() || this.split(),
  );

  /** A right column on a wide screen, for how long it takes and the close games, when there are any. */
  protected readonly split = computed(
    () => !!this.game.data()?.game_data?.time_to_beat || this.similar().length > 0,
  );

  /** A game never launched has no time to show: the page says so in their place. */
  protected readonly neverLaunched = computed(() => this.game.data()?.playtime_seconds === 0);

  /**
   * What was asked of the library for the game shown, and nothing of another: another game
   * opened from this page is the same page, and the mutations stay with it.
   */
  private readonly asked = computed(() =>
    [this.takingOut, this.puttingBack, this.dismissing].filter(
      (mutation) => mutation.variables() === this.id(),
    ),
  );

  /** What the library's banner is waiting on, or refused. */
  protected readonly busy = computed(() => this.asked().some((mutation) => mutation.isPending()));
  protected readonly refusal = computed(
    () =>
      this.asked()
        .find((mutation) => mutation.isError())
        ?.error() ?? null,
  );

  /** The data the page's history is drawn from could not be read: its months, or the library's years. */
  protected readonly historyRefusal = computed(() => this.months.error() ?? this.years.error());

  /**
   * Back where the page was reached from, as it was left (its search, its filter): the
   * browser's own way back. A page opened from its address goes to the library instead. A
   * press that opens a new tab or window is the browser's, on the link's address.
   */
  protected goBack(event: MouseEvent): void {
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
      return;
    }
    event.preventDefault();
    if (this.cameFrom() !== null) this.location.back();
    else void this.router.navigateByUrl(ORIGINS[this.origin()]);
  }

  protected retryHistory(): void {
    if (this.months.isError()) void this.months.refetch();
    if (this.years.isError()) void this.years.refetch();
  }

  protected pick(month: MonthKey): void {
    this.picked.set(month);
  }

  /** Taken out from here: the banner that follows offers to undo it. */
  protected readonly justTakenOut = computed(
    () => this.takingOut.isSuccess() && this.takingOut.variables() === this.id(),
  );

  protected takeOut(): void {
    this.reset();
    this.takingOut.mutate(this.id());
  }

  protected putBack(): void {
    this.reset();
    this.puttingBack.mutate(this.id());
  }

  protected dismiss(): void {
    this.reset();
    this.dismissing.mutate(this.id());
  }

  /** The cover the account sees for the game, by its source; null for the game's own. */
  protected chooseCover(source: string | null): void {
    this.reset();
    this.choosing.mutate({ id: this.id(), source });
  }

  protected retryMonth(): void {
    void this.month.refetch();
  }

  /** A refusal speaks of the last thing asked. */
  private reset(): void {
    this.takingOut.reset();
    this.puttingBack.reset();
    this.dismissing.reset();
    this.choosing.reset();
  }
}
