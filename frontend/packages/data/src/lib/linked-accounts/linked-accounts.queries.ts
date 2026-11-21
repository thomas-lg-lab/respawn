import { Service, inject } from '@angular/core';
import { QueryClient, mutationOptions, queryOptions } from '@tanstack/angular-query';
import { Api } from '../api/api';
import { libraryKeys } from '../library/library.queries';
import type { LinkedAccount, LinkedAccounts, SyncRun } from '../models/models';

/** The keys of what the cache holds about the linked store accounts. */
export const linkedAccountKeys = {
  list: ['linked-accounts'] as const,
};

/** How often the accounts are asked again while a sync is awaited, in milliseconds. */
export const AWAITING_SYNC_INTERVAL = 5_000;

/**
 * How long the first reading of an account just linked is awaited, in milliseconds. The API
 * starts it within minutes; past this, something kept it from ending and waiting longer
 * would bring nothing.
 */
export const FIRST_SYNC_WAIT = 10 * 60_000;

/**
 * Whether a sync of this account is on its way at `now`: one is running, or the account was
 * just linked and its first reading has not ended yet.
 */
export function awaitsSync(account: LinkedAccount, now: number): boolean {
  const { running, last } = account.sync;
  return running || (last === null && now - Date.parse(account.linked_at) < FIRST_SYNC_WAIT);
}

/** The last finished run of each account, as one word: it changes when any sync ends. */
function lastRuns(accounts: LinkedAccount[] | undefined): string {
  return (accounts ?? []).map((account) => `${account.id}:${account.sync.last?.id}`).join();
}

/**
 * The server state of the linked store accounts: which ones, the state of their sync, and
 * "sync now". Linking one is a navigation, not a call (`steam-link.ts`).
 */
@Service()
export class LinkedAccountQueries {
  private readonly api = inject(Api);
  private readonly client = inject(QueryClient);

  /**
   * The linked accounts, each with the state of its sync. Asked again every few seconds
   * while a sync is awaited (`awaitsSync`: never for ever), and that is where the end of one
   * is learnt: the library it read is then stale.
   */
  list() {
    return queryOptions({
      queryKey: linkedAccountKeys.list,
      queryFn: async (): Promise<LinkedAccount[]> => {
        const known = this.client.getQueryData<LinkedAccount[]>(linkedAccountKeys.list);
        const { accounts } = await this.api.get<LinkedAccounts>('/linked-accounts');
        if (known && lastRuns(known) !== lastRuns(accounts)) this.libraryChanged();
        return accounts;
      },
      // "Now" is when the accounts were last told: the screen reads the same moment, so the
      // answer that ends the wait is also the one it shows.
      refetchInterval: ({ state }) =>
        state.data?.some((account) => awaitsSync(account, state.dataUpdatedAt))
          ? AWAITING_SYNC_INTERVAL
          : false,
      retry: false,
    });
  }

  /**
   * The account that was never read and that nothing reads yet, once the accounts are known:
   * the one just linked, whose first reading can be asked without waiting for the schedule.
   * None when the accounts cannot be told.
   */
  async neverRead(): Promise<LinkedAccount | undefined> {
    const accounts = await this.client
      .ensureQueryData(this.list())
      .catch((): LinkedAccount[] => []);
    return accounts.find((account) => !account.sync.last && !account.sync.running);
  }

  /**
   * Reads the account's library now. The API answers once the reading ended, with the run:
   * one that failed is an answer too, and says why. Whatever came of it, the accounts are
   * asked again, and that is where a run that ended makes the library stale: once.
   */
  syncNow() {
    return mutationOptions({
      mutationFn: (accountId: string) =>
        this.api.post<SyncRun>(`/linked-accounts/${encodeURIComponent(accountId)}/sync`, {}),
      onSettled: () => this.client.invalidateQueries({ queryKey: linkedAccountKeys.list }),
    });
  }

  private libraryChanged(): void {
    void this.client.invalidateQueries({ queryKey: libraryKeys.everything });
  }
}
