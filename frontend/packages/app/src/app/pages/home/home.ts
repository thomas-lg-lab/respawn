import { Location } from '@angular/common';
import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideRefreshCw } from '@lucide/angular';
import {
  ApiFailure,
  type LibraryGame,
  LibraryQueries,
  LinkedAccountQueries,
  STEAM_LINK_OUTCOME,
  steamLinkAddress,
  steamLinkOutcome,
} from '@respawn/data';
import { Banner, Button, Label, Skeleton } from '@respawn/ui';
import { injectMutation, injectQuery } from '@tanstack/angular-query';
import { failureKey } from '@app/errors/failure-message';
import { formatMoment } from '@app/format/format';
import { LanguageSetting } from '@app/i18n/language';
import { LIBRARY_SEARCH, saidOnce } from '@app/session/said-once';
import { Activity } from './components/activity';
import { Figures } from './components/figures';
import { FirstSync } from './components/first-sync';
import { GameSearch } from './components/game-search';
import { NoAccount } from './components/no-account';
import { PlayedRecently } from './components/played-recently';
import { RandomPicks } from './components/random-picks';
import { Resume } from './components/resume';
import { SyncBanner } from './components/sync-banner';
import { SyncIssue } from './components/sync-issue';
import { TimeRanking } from './components/time-ranking';
import { Years } from './components/years';
import { activity, playedYears } from './history-blocks';
import {
  PICKS,
  drawn,
  keptDraw,
  lastPlayed,
  neverLaunched,
  playedRecently,
  ranking,
} from './library-blocks';
import {
  type SyncView,
  accountInView,
  syncErrorKey,
  syncNotice,
  syncView,
  syncsWord,
} from './sync-view';

/**
 * The refusals of a reading nobody asked for (the one made on the return from Steam) that
 * are not worth a word: something else reads the account, or just did.
 */
const READ_ANYWAY = ['sync_in_progress', 'too_many_requests'];

/**
 * What the screen shows: `loading`, then `unavailable` when the API could not say which
 * accounts are linked, else what their syncs make of it (`SyncView`).
 */
type View = 'loading' | 'unavailable' | SyncView;

/**
 * The first screen after sign-in: the library at a glance once a linked account was read (a
 * search, the game to resume, the ones played lately, the figures, the years, where the time
 * went, a draw among the never launched, the activity), and until then the way there, said state by state: link an
 * account, wait for its first reading, or learn why that reading brought no game. This is
 * where the states meet the API: the linked accounts and their syncs, "sync now", the
 * figures, the games, the history.
 *
 * Linking Steam is a navigation: the API sends the browser there and back to this screen,
 * which says what came of it, and reads the new account's library at once: the schedule
 * would only come to it minutes later.
 */
@Component({
  selector: 'rs-home',
  imports: [
    TranslocoDirective,
    LucideRefreshCw,
    Banner,
    Button,
    Label,
    Skeleton,
    Activity,
    Figures,
    FirstSync,
    GameSearch,
    NoAccount,
    PlayedRecently,
    RandomPicks,
    Resume,
    SyncBanner,
    SyncIssue,
    TimeRanking,
    Years,
  ],
  templateUrl: './home.html',
})
export class Home {
  private readonly library = inject(LibraryQueries);
  private readonly linked = inject(LinkedAccountQueries);
  private readonly language = inject(LanguageSetting);
  private readonly router = inject(Router);

  protected readonly failureKey = failureKey;
  protected readonly currentLanguage = this.language.current;
  protected readonly tiles = [0, 1, 2];

  protected readonly accounts = injectQuery(() => this.linked.list());

  /**
   * When the accounts were last told: the "now" a wait or an age is measured against. It
   * moves with each answer, so what depends on time is looked at again when one comes.
   */
  private readonly told = computed(() => this.accounts.dataUpdatedAt());
  protected readonly syncing = injectMutation(() => this.linked.syncNow());

  protected readonly view = computed<View>(() => {
    const accounts = this.accounts.data();
    if (!accounts) return this.accounts.isError() ? 'unavailable' : 'loading';
    return syncView(accounts, this.syncing.isPending(), this.told());
  });

  /** The figures, asked once there is a library to count. */
  protected readonly stats = injectQuery(() => ({
    ...this.library.stats(),
    enabled: this.view() === 'ready',
  }));

  /**
   * The games, asked with the figures: the blocks under them are made of what the browser
   * holds, the same games the Library lists.
   */
  protected readonly games = injectQuery(() => ({
    ...this.library.games(),
    enabled: this.view() === 'ready',
  }));

  /**
   * What was played day by day since the first sync, asked with the rest: the years and
   * the activity are made of it.
   */
  protected readonly history = injectQuery(() => ({
    ...this.library.history(),
    enabled: this.view() === 'ready',
  }));

  /** The time played each year; none while nothing was played since the first sync. */
  protected readonly years = computed(() => {
    const history = this.history.data();
    return history ? playedYears(history, this.history.dataUpdatedAt()) : [];
  });

  /** The latest days something was played, each game as the library names it. */
  protected readonly days = computed(() => {
    const history = this.history.data();
    return history ? activity(history, this.games.data() ?? []) : [];
  });

  /** "Ago" is counted from the moment the games were read, so it follows each new reading. */
  protected readonly now = computed(() => this.games.dataUpdatedAt() || Date.now());

  /** The game to resume, the one played last; `null` while a store told no session. */
  protected readonly resumed = computed(() =>
    lastPlayed(this.games.data() ?? [], this.language.current()),
  );

  protected readonly recently = computed(() =>
    playedRecently(this.games.data() ?? [], this.resumed(), this.language.current()),
  );

  protected readonly ranked = computed(() =>
    ranking(this.games.data() ?? [], this.language.current()),
  );

  /** The games a draw is made among. */
  private readonly neverLaunched = computed(() => neverLaunched(this.games.data() ?? []));
  /** Another draw can only give other games when there are more than one draw shows. */
  protected readonly moreToDraw = computed(() => this.neverLaunched().length > PICKS);
  /**
   * The draw: made when the games arrive, and again on request. A new reading of the games
   * keeps it while its games can still be drawn.
   */
  protected readonly picks = linkedSignal<LibraryGame[], LibraryGame[]>({
    source: this.neverLaunched,
    computation: (pool, shown) => keptDraw(pool, shown?.value, PICKS),
  });

  /**
   * A "sync now" the API refused, and where the syncs stood when it did. It is said while
   * they stand there: once one starts or ends, or the visitor puts it away, it is no longer
   * true of the screen.
   */
  private readonly refused = signal<{ failure: unknown; stood: string } | null>(null);

  /**
   * Why a "sync now" was refused, as a translation key. Too soon after the last one has a
   * sentence of its own: the API takes one a minute, which is not "too many attempts".
   */
  protected readonly syncRefusal = computed(() => {
    const refused = this.refused();
    if (!refused || refused.stood !== syncsWord(this.accounts.data() ?? [])) return null;
    const { failure } = refused;
    return failure instanceof ApiFailure && failure.code === 'too_many_requests'
      ? 'home.sync.tooSoon'
      : failureKey(failure);
  });

  /** The account the screen speaks of, and the one "sync now" reads. */
  private readonly account = computed(() => accountInView(this.accounts.data() ?? []));

  /** The visitor put away the word about an old sync: not said again on this visit. */
  private readonly staleDismissed = signal(false);

  protected readonly notice = computed(() => {
    const account = this.account();
    if (!account) return null;
    const notice = syncNotice(account, this.syncing.isPending(), this.told());
    return notice === 'stale' && this.staleDismissed() ? null : notice;
  });

  /** Why the last reading failed, as a translation key. */
  protected readonly reason = computed(() => syncErrorKey(this.account()?.sync.last));

  /** When a reading last succeeded, written in the reader's language. */
  protected readonly lastSuccess = computed(() => {
    const moment = this.account()?.sync.last_succeeded_at;
    return moment ? formatMoment(moment, this.language.current()) : undefined;
  });

  /** The link through Steam comes back to this screen. */
  protected readonly linkAddress = steamLinkAddress('/');

  /**
   * What came of linking a Steam account, as the API wrote it on the address it sent the
   * browser back to. Said until the visitor moves on, and once: the word is taken off the
   * browser's address.
   */
  protected readonly cameBack = steamLinkOutcome(
    inject(ActivatedRoute).snapshot.queryParamMap.get(STEAM_LINK_OUTCOME),
  );

  constructor() {
    saidOnce(inject(Location), this.router, STEAM_LINK_OUTCOME);
    if (this.cameBack === 'linked') void this.readNewAccount();
  }

  /**
   * An account was just linked: its first reading is asked now. Once the accounts are known,
   * and only for one never read that nothing reads yet; when they cannot be told, the screen
   * says so and the schedule will read it. The schedule may also get there first: that
   * refusal is not said, the reading is on its way all the same.
   */
  private async readNewAccount(): Promise<void> {
    const unread = await this.linked.neverRead();
    if (unread) this.read(unread.id, READ_ANYWAY);
  }

  protected syncNow(): void {
    const account = this.account();
    if (account) this.read(account.id);
  }

  protected dismissRefusal(): void {
    this.refused.set(null);
  }

  /**
   * Asks for a reading. A refusal is kept with where the syncs stand once the accounts were
   * asked again (the mutation does that before it calls back), unless its code is `unsaid`.
   */
  private read(accountId: string, unsaid: string[] = []): void {
    this.refused.set(null);
    this.syncing.mutate(accountId, {
      onError: (failure) => {
        if (failure instanceof ApiFailure && unsaid.includes(failure.code)) return;
        this.refused.set({ failure, stood: syncsWord(this.accounts.data() ?? []) });
      },
    });
  }

  protected dismissStale(): void {
    this.staleDismissed.set(true);
  }

  /** A title was asked for: the Library looks for it. */
  protected search(title: string): void {
    void this.router.navigate(['/library'], {
      queryParams: title ? { [LIBRARY_SEARCH]: title } : {},
    });
  }

  protected drawAgain(): void {
    this.picks.set(drawn(this.neverLaunched(), PICKS));
  }
}
