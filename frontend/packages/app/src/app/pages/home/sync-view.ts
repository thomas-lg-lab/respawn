import { type LinkedAccount, type SyncRun, awaitsSync } from '@respawn/data';

/**
 * What the home shows, from the linked accounts and their syncs:
 * - `no-account`: nothing is linked yet, the screen leads to the link;
 * - `first-sync`: an account is linked and no reading of it ever succeeded, one is on its way;
 * - `sync-failed`, `nothing-visible`: the first reading ended and brought no game, because
 *   it failed or because the store showed none;
 * - `ready`: a reading succeeded once, there is a library to show whatever happened since.
 */
export type SyncView = 'no-account' | 'first-sync' | 'sync-failed' | 'nothing-visible' | 'ready';

/** `syncing`: a "sync now" asked from this screen has not answered yet. */
export function syncView(accounts: LinkedAccount[], syncing: boolean): SyncView {
  if (accounts.length === 0) return 'no-account';
  if (accounts.some((account) => account.sync.last_succeeded_at)) return 'ready';
  if (syncing || accounts.some(awaitsSync)) return 'first-sync';
  return accounts.some((account) => account.sync.last?.outcome === 'failed')
    ? 'sync-failed'
    : 'nothing-visible';
}

/** After this long without a successful reading, two daily ones were missed. */
export const STALE_AFTER = 48 * 60 * 60 * 1000;

/**
 * What the home says above a library it shows:
 * - `running`: a sync is on its way, the games shown are the last reading's;
 * - `failed`, `nothing-visible`: the last reading brought nothing, the games shown are older;
 * - `stale`: nothing went wrong that the app knows of, and yet the last success is old.
 */
export type SyncNotice = 'running' | 'failed' | 'nothing-visible' | 'stale';

export function syncNotice(
  account: LinkedAccount,
  syncing: boolean,
  now: number,
): SyncNotice | null {
  const { running, last, last_succeeded_at } = account.sync;
  if (syncing || running) return 'running';
  if (last?.outcome === 'failed') return 'failed';
  if (last?.outcome === 'nothing_visible') return 'nothing-visible';
  if (last_succeeded_at && now - Date.parse(last_succeeded_at) > STALE_AFTER) return 'stale';
  return null;
}

/**
 * The account the home speaks of and syncs: the one whose last reading went wrong if any,
 * else the one read the longest ago.
 */
export function accountInView(accounts: LinkedAccount[]): LinkedAccount | undefined {
  const troubled = accounts.find((account) => account.sync.last?.outcome === 'failed');
  const unseen = accounts.find((account) => account.sync.last?.outcome === 'nothing_visible');
  const oldestFirst = [...accounts].sort((one, other) =>
    (one.sync.last_succeeded_at ?? '').localeCompare(other.sync.last_succeeded_at ?? ''),
  );
  return troubled ?? unseen ?? oldestFirst[0];
}

/** The reasons of a failed run the home has a sentence for. */
const SYNC_ERRORS = ['not_configured', 'refused', 'unavailable', 'unreadable'];

/**
 * The translation key of why a run failed. A key the user gave and the store refused is
 * theirs to change, which is another sentence than the instance's key refused.
 */
export function syncErrorKey(run: SyncRun | null | undefined): string {
  const error = run?.error ?? '';
  if (error === 'refused' && run?.own_key) return 'home.syncErrors.refused_own_key';
  return `home.syncErrors.${SYNC_ERRORS.includes(error) ? error : 'unknown'}`;
}
