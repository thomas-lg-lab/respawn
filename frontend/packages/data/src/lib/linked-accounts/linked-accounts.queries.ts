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
 * Whether a sync of this account is on its way: one is running, or the account was just
 * linked and its first reading has not ended yet (the API starts it within minutes).
 */
export function awaitsSync(account: LinkedAccount): boolean {
  return account.sync.running || account.sync.last === null;
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
   * while a sync is awaited, and that is where the end of one is learnt: the library it
   * read is then stale.
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
      refetchInterval: (query) =>
        query.state.data?.some(awaitsSync) ? AWAITING_SYNC_INTERVAL : false,
      retry: false,
    });
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
