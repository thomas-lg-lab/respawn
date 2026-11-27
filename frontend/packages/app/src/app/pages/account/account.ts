import { Location } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideLink } from '@lucide/angular';
import {
  AccountQueries,
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
import { syncErrorKey, syncRefusalKey } from '@app/sync/sync-view';
import { saidOnce } from '@app/session/said-once';
import { type AccountNotice, LinkedAccount } from './components/linked-account';
import { Registration } from './components/registration';
import type { GivenKey } from './components/own-key-form';

/** What a card may be waiting for: a reading, its key kept or taken away, its unlinking. */
type Asked = 'sync' | 'key' | 'unlink';

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
    Registration,
  ],
  templateUrl: './account.html',
})
export class Account {
  private readonly queries = inject(AccountQueries);
  private readonly linked = inject(LinkedAccountQueries);
  private readonly language = inject(LanguageSetting);

  protected readonly failureKey = failureKey;
  protected readonly currentLanguage = this.language.current;

  protected readonly me = injectQuery(() => this.queries.me());
  protected readonly accounts = injectQuery(() => this.linked.list());

  /** What the instance offers, for its administrator alone: whether registration is open. */
  protected readonly offered = injectQuery(() => ({
    ...this.queries.signInOptions(),
    enabled: this.me.data()?.is_admin === true,
  }));
  protected readonly settingRegistration = injectMutation(() => this.queries.setRegistration());

  private readonly syncing = injectMutation(() => this.linked.syncNow());
  private readonly unlinking = injectMutation(() => this.linked.unlink());
  private readonly givingKey = injectMutation(() => this.linked.giveKey());
  private readonly removingKey = injectMutation(() => this.linked.removeKey());

  /**
   * What the cards wait for, as `what:account`. Kept here and not read from the mutations:
   * each of them follows its last call only, and two cards may wait at once.
   */
  private readonly pending = signal<ReadonlySet<string>>(new Set());

  /** What came of the last thing asked of each account, by its id. */
  private readonly notices = signal<ReadonlyMap<string, AccountNotice>>(new Map());

  /** What is said of the screen itself: an account that is no longer there has no card. */
  protected readonly screenNotice = signal<AccountNotice | null>(null);

  /** The accounts unlinked from here: what was still asked of them is no longer worth a word. */
  private readonly unlinked = new Set<string>();

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
    return this.notices().get(account.id) ?? null;
  }

  /** Whether a sync of this account is on its way: asked here, running, or its first awaited. */
  protected isSyncing(account: LinkedStoreAccount): boolean {
    return this.waits('sync', account.id) || awaitsSync(account, this.accounts.dataUpdatedAt());
  }

  protected isSavingKey(account: LinkedStoreAccount): boolean {
    return this.waits('key', account.id);
  }

  protected isUnlinking(account: LinkedStoreAccount): boolean {
    return this.waits('unlink', account.id);
  }

  /** Why the last reading of this account failed, as a translation key; `null` when it did not. */
  protected reasonOf(account: LinkedStoreAccount): string | null {
    const { last } = account.sync;
    return last?.outcome === 'failed' ? syncErrorKey(last) : null;
  }

  /** Reads the account now. A run that ended is not said: the card shows what it left. */
  protected syncNow(accountId: string): void {
    void this.ask('sync', accountId, () => this.syncing.mutateAsync(accountId), {
      refusal: syncRefusalKey,
    });
  }

  /**
   * Keeps the key. The form is told once the API took it, and only then lets go of what was
   * typed; nothing of the key stays here either, whatever the API answered.
   */
  protected giveKey(accountId: string, given: GivenKey): void {
    void this.ask(
      'key',
      accountId,
      () =>
        this.givingKey
          .mutateAsync({ accountId, key: given.key })
          .finally(() => this.givingKey.reset()),
      {
        done: () => {
          given.kept();
          this.say(accountId, { tone: 'status', key: 'account.key.kept' });
        },
      },
    );
  }

  protected removeKey(accountId: string): void {
    void this.ask('key', accountId, () => this.removingKey.mutateAsync(accountId), {
      done: () => this.say(accountId, { tone: 'status', key: 'account.key.removed' }),
    });
  }

  protected unlink(accountId: string): void {
    void this.ask('unlink', accountId, () => this.unlinking.mutateAsync(accountId), {
      done: () => {
        this.unlinked.add(accountId);
        this.say(accountId, null);
        this.screenNotice.set({ tone: 'status', key: 'account.unlink.done' });
      },
    });
  }

  private waits(what: Asked, accountId: string): boolean {
    return this.pending().has(`${what}:${accountId}`);
  }

  /**
   * Asks something of one account and says what came of it, on its card. The card waits
   * until the API answered and the accounts were asked again (each mutation does that
   * before it ends), whatever the other cards ask meanwhile. `refusal` picks the sentence
   * of a refusal, or none when it is not worth a word.
   */
  private async ask(
    what: Asked,
    accountId: string,
    work: () => Promise<unknown>,
    {
      done,
      refusal = failureKey,
    }: { done?: () => void; refusal?: (failure: unknown) => string | null } = {},
  ): Promise<void> {
    const word = `${what}:${accountId}`;
    if (this.pending().has(word)) return;

    this.say(accountId, null);
    this.screenNotice.set(null);
    this.pending.update((all) => new Set(all).add(word));
    try {
      await work();
      done?.();
    } catch (failure) {
      this.refused(accountId, refusal(failure));
    } finally {
      this.pending.update((all) => new Set([...all].filter((other) => other !== word)));
    }
  }

  /** Says this of the account on its card, in place of what was said there; `null` says nothing. */
  private say(accountId: string, notice: AccountNotice | null): void {
    this.notices.update((all) => {
      const said = new Map(all);
      if (notice) said.set(accountId, notice);
      else said.delete(accountId);
      return said;
    });
  }

  /**
   * A refusal is said on the account's card, or on the screen when the account is no longer
   * there (unlinked elsewhere: the accounts were asked again, and its card is gone). Of an
   * account unlinked from here nothing more is said: what it still waited for went with it.
   */
  private refused(accountId: string, key: string | null): void {
    if (!key || this.unlinked.has(accountId)) return;

    const notice: AccountNotice = { tone: 'warn', key };
    if (this.accounts.data()?.some((account) => account.id === accountId)) {
      this.say(accountId, notice);
    } else {
      this.screenNotice.set(notice);
    }
  }

  /**
   * An account was just linked: its first reading is asked now, since the schedule would
   * only come to it minutes later. A refusal is not said: the schedule will read it.
   */
  private async readNewAccount(): Promise<void> {
    const unread = await this.linked.neverRead();
    if (unread) {
      await this.ask('sync', unread.id, () => this.syncing.mutateAsync(unread.id), {
        refusal: () => null,
      });
    }
  }
}
