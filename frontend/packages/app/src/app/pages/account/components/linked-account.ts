import { Component, computed, input, output, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideRefreshCw, LucideUnlink } from '@lucide/angular';
import type { LinkedAccount as LinkedStoreAccount } from '@respawn/data';
import { Banner, Button, Card } from '@respawn/ui';
import { formatMoment } from '@app/format/format';
import { type GivenKey, OwnKeyForm } from './own-key-form';

/** What the screen says of one linked account: the key of a sentence, and whether it is a refusal. */
export interface AccountNotice {
  readonly tone: 'status' | 'warn';
  readonly key: string;
}

/**
 * One linked store account: which one, since when, where its sync stands and which key reads
 * it, with what its owner may ask: a reading now, a key of their own, and unlinking it.
 *
 * Unlinking cannot be undone, so it is asked twice: the first button only shows what will be
 * forgotten, the second one asks for it.
 */
@Component({
  selector: 'rs-linked-account',
  imports: [TranslocoDirective, Banner, Button, Card, OwnKeyForm, LucideRefreshCw, LucideUnlink],
  templateUrl: './linked-account.html',
})
export class LinkedAccount {
  readonly account = input.required<LinkedStoreAccount>();
  readonly language = input.required<string>();
  /** A sync of this account is on its way. */
  readonly syncing = input(false);
  /** Why the last reading failed, as a translation key; none when it did not. */
  readonly reason = input<string | null>(null);
  /** What came of the last thing asked of this account. */
  readonly notice = input<AccountNotice | null>(null);
  /** While a key is being kept or taken away. */
  readonly savingKey = input(false);
  /** While the account is being unlinked. */
  readonly unlinking = input(false);

  readonly syncNow = output<void>();
  readonly keyGiven = output<GivenKey>();
  readonly keyRemoved = output<void>();
  readonly unlink = output<void>();

  /** The visitor asked to unlink, and has not confirmed yet. */
  protected readonly confirming = signal(false);

  /** The name the store gave, or the account's id there when it gave none. */
  protected readonly name = computed(
    () => this.account().display_name ?? this.account().external_id,
  );

  protected readonly linkedAt = computed(() =>
    formatMoment(this.account().linked_at, this.language()),
  );

  protected readonly lastSuccess = computed(() => {
    const moment = this.account().sync.last_succeeded_at;
    return moment ? formatMoment(moment, this.language()) : null;
  });
}
