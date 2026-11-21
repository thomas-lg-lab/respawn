import type { Account, LibraryStats, LinkedAccount, SyncRun } from '../models/models';

/** For the specs, here and in the packages that use this one: an account as the API describes one. */
export const TEST_ACCOUNT: Account = {
  id: '0b0f6f3e-6a59-4c8c-9a55-2d7f3f0d6c11',
  email: 'first@example.org',
  display_name: 'First',
  is_admin: true,
  settings: null,
};

/** A reading of the library that went well, early in the morning. */
export const TEST_SYNC_RUN: SyncRun = {
  id: '5f0c1f0a-3a0e-4f0b-8a53-0d5f6a1f7c21',
  linked_account_id: '9d2b7a44-1c53-4b7e-9c0a-6e1f2a3b4c5d',
  trigger: 'scheduled',
  started_at: '2026-11-20T04:02:00Z',
  finished_at: '2026-11-20T04:02:05Z',
  outcome: 'succeeded',
  error: null,
  own_key: false,
};

/** A Steam account linked a while ago, its library read. */
export const TEST_LINKED_ACCOUNT: LinkedAccount = {
  id: '9d2b7a44-1c53-4b7e-9c0a-6e1f2a3b4c5d',
  source: 'steam',
  external_id: '76561198000000001',
  display_name: null,
  linked_at: '2026-11-18T10:00:00Z',
  has_own_key: false,
  sync: { running: false, last: TEST_SYNC_RUN, last_succeeded_at: TEST_SYNC_RUN.finished_at },
};

/** The figures of a library that has been played. */
export const TEST_LIBRARY_STATS: LibraryStats = {
  games: 1305,
  launched: 861,
  never_launched: 444,
  missing: 0,
  playtime_seconds: 2203200,
  recent_playtime_seconds: 50400,
  recently_played: 3,
  tracking_since: '2026-11-18T10:05:00Z',
};
