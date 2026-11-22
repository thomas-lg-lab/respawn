import { Location } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideLink } from '@lucide/angular';
import {
  AccountQueries,
  ApiFailure,
  type LinkedAccount as LinkedStoreAccount,
  LinkedAccountQueries,
  STEAM_LINK_OUTCOME,
  awaitsSync,
  steamLinkAddress,
  steamLinkOutcome,
} from '@respawn/data';
import { Avatar, Banner, Button, Card, Label, Skeleton } from '@respawn/ui';
import { injectMutation, injectQuery } from '@tanstack/angular-query';
import { failureKey } from '@app/errors/failure-message';
import { LanguageSetting } from '@app/i18n/language';
import { syncErrorKey } from '@app/pages/home/sync-view';
import { saidOnce } from '@app/session/said-once';
import { type AccountNotice, LinkedAccount } from './components/linked-account';

/** What is said of one linked account, or of the screen when `accountId` is `null`. */
interface Said extends AccountNotice {
  readonly accountId: string | null;
}

/**
 * The account: who is signed in, and the store accounts linked to it, each with where its
 * sync stands, the key that reads it and the way to unlink it. This is where what a card
 * asks meets the API, and where what came of it is said, on the card it was asked from.
 *
 * Linking Steam is a navigation that comes back to this screen, which says what came of it
 * and reads the new account's library at once, as the home does.
 */
@Component({
  selector: 'rs-account',
  imports: [
    TranslocoDirective,
    LucideLink,
    Avatar,
    Banner,
    Button,
    Card,
    Label,
    Skeleton,
    LinkedAccount,
  ],
  templateUrl: './account.html',
})
export class Account {
  private readonly queries = inject(AccountQueries);
  private readonly linked = inject(LinkedAccountQueries);
  private readonly language = inject(LanguageSetting);

  protected readonly failureKey = failureKey;
  protected readonly syncErrorKey = syncErrorKey;
  protected readonly currentLanguage = this.language.current;

  protected readonly me = injectQuery(() => this.queries.me());
  protected readonly accounts = injectQuery(() => this.linked.list());

  protected readonly syncing = injectMutation(() => this.linked.syncNow());
  protected readonly unlinking = injectMutation(() => this.linked.unlink());
  protected readonly givingKey = injectMutation(() => this.linked.giveKey());
  protected readonly removingKey = injectMutation(() => this.linked.removeKey());

  /** What came of the last thing asked here. One sentence at a time: the next one replaces it. */
  private readonly said = signal<Said | null>(null);

  /** What is said of the screen itself: an account that is no longer there has no card. */
  protected readonly screenNotice = computed(() => {
    const said = this.said();
    return said?.accountId === null ? said : null;
  });

  /** The link through Steam comes back to this screen. */
  protected readonly linkAddress = steamLinkAddress('/account');

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

  /** What is said on the card of this account, if anything. */
  protected noticeOf(account: LinkedStoreAccount): AccountNotice | null {
    const said = this.said();
    return said?.accountId === account.id ? said : null;
  }

  /** Whether a sync of this account is on its way: asked here, running, or its first awaited. */
  protected isSyncing(account: LinkedStoreAccount): boolean {
    return (
      (this.syncing.isPending() && this.syncing.variables() === account.id) ||
      awaitsSync(account, this.accounts.dataUpdatedAt())
    );
  }

  protected isSavingKey(account: LinkedStoreAccount): boolean {
    return (
      (this.givingKey.isPending() && this.givingKey.variables()?.accountId === account.id) ||
      (this.removingKey.isPending() && this.removingKey.variables() === account.id)
    );
  }

  protected isUnlinking(account: LinkedStoreAccount): boolean {
    return this.unlinking.isPending() && this.unlinking.variables() === account.id;
  }

  /** Why the last reading of this account failed, as a translation key; `null` when it did not. */
  protected reasonOf(account: LinkedStoreAccount): string | null {
    const { last } = account.sync;
    return last?.outcome === 'failed' ? syncErrorKey(last) : null;
  }

  /**
   * Reads the account now. A run that ended is not said: the card shows what it left. Too
   * soon after the last one has a sentence of its own, as on the home.
   */
  protected syncNow(accountId: string): void {
    this.said.set(null);
    this.syncing.mutate(accountId, {
      onError: (failure) =>
        this.refused(
          accountId,
          failure instanceof ApiFailure && failure.code === 'too_many_requests'
            ? 'home.sync.tooSoon'
            : failureKey(failure),
        ),
    });
  }

  protected giveKey(accountId: string, key: string): void {
    this.said.set(null);
    this.givingKey.mutate(
      { accountId, key },
      {
        onSuccess: () => this.said.set({ accountId, tone: 'status', key: 'account.key.kept' }),
        onError: (failure) => this.refused(accountId, failureKey(failure)),
      },
    );
  }

  protected removeKey(accountId: string): void {
    this.said.set(null);
    this.removingKey.mutate(accountId, {
      onSuccess: () => this.said.set({ accountId, tone: 'status', key: 'account.key.removed' }),
      onError: (failure) => this.refused(accountId, failureKey(failure)),
    });
  }

  protected unlink(accountId: string): void {
    this.said.set(null);
    this.unlinking.mutate(accountId, {
      onSuccess: () =>
        this.said.set({ accountId: null, tone: 'status', key: 'account.unlink.done' }),
      onError: (failure) => this.refused(accountId, failureKey(failure)),
    });
  }

  private refused(accountId: string, key: string): void {
    this.said.set({ accountId, tone: 'warn', key });
  }

  /**
   * An account was just linked: its first reading is asked now, since the schedule would
   * only come to it minutes later. A refusal is not said: the schedule will read it.
   */
  private async readNewAccount(): Promise<void> {
    const unread = await this.linked.neverRead();
    if (unread) this.syncing.mutate(unread.id);
  }
}
