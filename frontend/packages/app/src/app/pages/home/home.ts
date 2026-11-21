import { Location } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideRefreshCw } from '@lucide/angular';
import {
  ApiFailure,
  LibraryQueries,
  LinkedAccountQueries,
  STEAM_LINK_OUTCOME,
  steamLinkAddress,
  steamLinkOutcome,
} from '@respawn/data';
import { Banner, Button, Label, Skeleton } from '@respawn/ui';
import { QueryClient, injectMutation, injectQuery } from '@tanstack/angular-query';
import { failureKey } from '@app/errors/failure-message';
import { formatMoment } from '@app/format/format';
import { LanguageSetting } from '@app/i18n/language';
import { saidOnce } from '@app/session/said-once';
import { Figures } from './components/figures';
import { FirstSync } from './components/first-sync';
import { NoAccount } from './components/no-account';
import { SyncBanner } from './components/sync-banner';
import { SyncIssue } from './components/sync-issue';
import { type SyncView, accountInView, syncErrorKey, syncNotice, syncView } from './sync-view';

/**
 * What the screen shows: `loading`, then `unavailable` when the API could not say which
 * accounts are linked, else what their syncs make of it (`SyncView`).
 */
type View = 'loading' | 'unavailable' | SyncView;

/**
 * The first screen after sign-in: the library in figures once a linked account was read, and
 * until then the way there, said state by state: link an account, wait for its first reading,
 * or learn why that reading brought no game. This is where the states meet the API: the
 * linked accounts and their syncs, "sync now", the figures.
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
    Figures,
    FirstSync,
    NoAccount,
    SyncBanner,
    SyncIssue,
  ],
  templateUrl: './home.html',
})
export class Home {
  private readonly library = inject(LibraryQueries);
  private readonly linked = inject(LinkedAccountQueries);
  private readonly language = inject(LanguageSetting);
  private readonly client = inject(QueryClient);

  protected readonly failureKey = failureKey;
  protected readonly currentLanguage = this.language.current;
  protected readonly tiles = [0, 1, 2];

  protected readonly accounts = injectQuery(() => this.linked.list());
  protected readonly syncing = injectMutation(() => this.linked.syncNow());

  protected readonly view = computed<View>(() => {
    const accounts = this.accounts.data();
    if (!accounts) return this.accounts.isError() ? 'unavailable' : 'loading';
    return syncView(accounts, this.syncing.isPending());
  });

  /** The figures, asked once there is a library to count. */
  protected readonly stats = injectQuery(() => ({
    ...this.library.stats(),
    enabled: this.view() === 'ready',
  }));

  /**
   * Why a "sync now" was refused, as a translation key. Too soon after the last one has a
   * sentence of its own: the API takes one a minute, which is not "too many attempts".
   */
  protected readonly syncRefusal = computed(() => {
    const refusal = this.syncing.error();
    if (!refusal) return null;
    return refusal instanceof ApiFailure && refusal.code === 'too_many_requests'
      ? 'home.sync.tooSoon'
      : failureKey(refusal);
  });

  /** The account the screen speaks of, and the one "sync now" reads. */
  private readonly account = computed(() => accountInView(this.accounts.data() ?? []));

  /** The visitor put away the word about an old sync: not said again on this visit. */
  private readonly staleDismissed = signal(false);

  protected readonly notice = computed(() => {
    const account = this.account();
    if (!account) return null;
    const notice = syncNotice(account, this.syncing.isPending(), Date.now());
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
    saidOnce(inject(Location), inject(Router), STEAM_LINK_OUTCOME);
    if (this.cameBack === 'linked') void this.readNewAccount();
  }

  /**
   * An account was just linked: its first reading is asked now. Once the accounts are known,
   * and only for one never read that nothing reads yet; when they cannot be told, the screen
   * says so and the schedule will read it.
   */
  private async readNewAccount(): Promise<void> {
    const accounts = await this.client.ensureQueryData(this.linked.list()).catch(() => []);
    const unread = accounts.find((account) => !account.sync.last && !account.sync.running);
    if (unread) this.syncing.mutate(unread.id);
  }

  protected syncNow(): void {
    const account = this.account();
    if (account) this.syncing.mutate(account.id);
  }

  protected dismissStale(): void {
    this.staleDismissed.set(true);
  }
}
