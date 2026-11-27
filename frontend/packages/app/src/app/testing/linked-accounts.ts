import type { LinkedAccount, SyncRun } from '@respawn/data';
import { TEST_LINKED_ACCOUNT, TEST_SYNC_RUN } from '@respawn/data/testing';

let runs = 0;

/** A reading that just succeeded: nothing to say about the sync. Each is a run of its own. */
export function succeeded(at = new Date()): SyncRun {
  const id = `5f0c1f0a-3a0e-4f0b-8a53-${String(++runs).padStart(12, '0')}`;
  return { ...TEST_SYNC_RUN, id, started_at: at.toISOString(), finished_at: at.toISOString() };
}

/**
 * A linked account whose library was read a moment ago, so that nothing is awaited, unless
 * `sync` says otherwise of its sync, or `values` of the account itself.
 */
export function linkedAccount(
  sync: Partial<LinkedAccount['sync']> = {},
  values: Partial<LinkedAccount> = {},
): LinkedAccount {
  const last = succeeded();
  return {
    ...TEST_LINKED_ACCOUNT,
    sync: { running: false, last, last_succeeded_at: last.finished_at, ...sync },
    ...values,
  };
}
