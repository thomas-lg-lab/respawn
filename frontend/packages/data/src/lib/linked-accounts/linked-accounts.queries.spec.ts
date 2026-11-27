import type { HttpTestingController } from '@angular/common/http/testing';
import { MutationObserver, type QueryClient } from '@tanstack/angular-query';
import { libraryKeys } from '../library/library.queries';
import type { LinkedAccount } from '../models/models';
import {
  TEST_LINKED_ACCOUNT as ACCOUNT,
  TEST_LIBRARY_STATS,
  TEST_SYNC_RUN,
} from '../testing/fixtures';
import { type QueriesSetUp, setUpQueries } from '../testing/queries';
import {
  AWAITING_SYNC_INTERVAL,
  FIRST_SYNC_WAIT,
  LinkedAccountQueries,
  awaitsSync,
  linkedAccountKeys,
} from './linked-accounts.queries';
import { steamLinkAddress, steamLinkOutcome } from './steam-link';

const NOW = Date.parse('2026-11-21T12:00:00Z');

const JUST_LINKED: LinkedAccount = {
  ...ACCOUNT,
  linked_at: '2026-11-21T11:59:00Z',
  sync: { running: false, last: null, last_succeeded_at: null },
};

/** Another store account of the same owner, linked a moment ago. */
const JUST_LINKED_ELSEWHERE: LinkedAccount = { ...JUST_LINKED, id: 'elsewhere' };

describe('LinkedAccountQueries', () => {
  let queries: LinkedAccountQueries;
  let client: QueryClient;
  let http: HttpTestingController;
  let request: QueriesSetUp<LinkedAccountQueries>['request'];

  beforeEach(() => {
    ({ queries, client, http, request } = setUpQueries(LinkedAccountQueries));
  });

  afterEach(() => http.verify());

  const stale = (key: readonly string[]) => client.getQueryState(key)?.isInvalidated;

  it('reads the linked accounts', async () => {
    const accounts = client.fetchQuery(queries.list());
    (await request('/api/v1/linked-accounts')).flush({ accounts: [ACCOUNT] });

    expect(await accounts).toEqual([ACCOUNT]);
  });

  it('awaits a sync that runs, and the first one of an account just linked', () => {
    expect(awaitsSync(ACCOUNT, NOW)).toBe(false);
    expect(awaitsSync(JUST_LINKED, NOW)).toBe(true);
    expect(awaitsSync({ ...ACCOUNT, sync: { ...ACCOUNT.sync, running: true } }, NOW)).toBe(true);
  });

  it('stops awaiting a first reading that left nothing behind', () => {
    expect(awaitsSync(JUST_LINKED, NOW + FIRST_SYNC_WAIT - 60_000 - 1)).toBe(true);
    expect(awaitsSync(JUST_LINKED, NOW + FIRST_SYNC_WAIT)).toBe(false);
  });

  it('asks again every few seconds only while a sync is awaited', () => {
    const interval = queries.list().refetchInterval as (query: {
      state: { data?: LinkedAccount[]; dataUpdatedAt: number };
    }) => number | false;
    const told = (data: LinkedAccount[] | undefined, dataUpdatedAt = NOW) =>
      interval({ state: { data, dataUpdatedAt } });

    expect(told([JUST_LINKED])).toBe(AWAITING_SYNC_INTERVAL);
    expect(told([ACCOUNT])).toBe(false);
    expect(told(undefined, 0)).toBe(false);
    // A first reading awaited long enough: the accounts are no longer asked for it.
    expect(told([JUST_LINKED], NOW + FIRST_SYNC_WAIT)).toBe(false);
  });

  it('finds the account never read that nothing reads yet', async () => {
    const running = { ...JUST_LINKED, id: 'running', sync: { ...JUST_LINKED.sync, running: true } };
    const found = queries.neverRead();
    (await request('/api/v1/linked-accounts')).flush({ accounts: [ACCOUNT, running, JUST_LINKED] });

    expect((await found)?.id).toBe(JUST_LINKED.id);
    expect(JUST_LINKED.id).not.toBe('running');
  });

  it('finds no account to read when they cannot be told', async () => {
    const found = queries.neverRead();
    (await request('/api/v1/linked-accounts')).flush(
      { error: { code: 'database_unavailable', message: 'In English, for the log.' } },
      { status: 503, statusText: '' },
    );

    expect(await found).toBeUndefined();
  });

  it('marks the library stale when it learns that a sync ended', async () => {
    client.setQueryData(linkedAccountKeys.list, [JUST_LINKED]);
    client.setQueryData(libraryKeys.stats, TEST_LIBRARY_STATS);

    const accounts = client.fetchQuery({ ...queries.list(), staleTime: 0 });
    (await request('/api/v1/linked-accounts')).flush({ accounts: [ACCOUNT] });
    await accounts;

    expect(stale(libraryKeys.stats)).toBe(true);
  });

  it('leaves the library alone while no sync ended', async () => {
    client.setQueryData(linkedAccountKeys.list, [ACCOUNT]);
    client.setQueryData(libraryKeys.stats, TEST_LIBRARY_STATS);

    const accounts = client.fetchQuery({ ...queries.list(), staleTime: 0 });
    (await request('/api/v1/linked-accounts')).flush({ accounts: [ACCOUNT] });
    await accounts;

    expect(stale(libraryKeys.stats)).toBe(false);
  });

  it('syncs now, then asks again for the accounts', async () => {
    client.setQueryData(linkedAccountKeys.list, [ACCOUNT]);
    const sync = new MutationObserver(client, queries.syncNow());

    const done = sync.mutate(ACCOUNT.id);
    const sent = await request(`/api/v1/linked-accounts/${ACCOUNT.id}/sync`);
    expect(sent.request.method).toBe('POST');
    sent.flush({ ...TEST_SYNC_RUN, trigger: 'manual' });

    expect(await done).toMatchObject({ outcome: 'succeeded', trigger: 'manual' });
    expect(stale(linkedAccountKeys.list)).toBe(true);
  });

  it('fails with the code of the API when a sync is refused', async () => {
    const sync = new MutationObserver(client, queries.syncNow());

    const done = sync.mutate(ACCOUNT.id).catch((thrown: unknown) => thrown);
    (await request(`/api/v1/linked-accounts/${ACCOUNT.id}/sync`)).flush(
      { error: { code: 'sync_in_progress', message: 'In English, for the log.' } },
      { status: 409, statusText: '' },
    );

    expect(await done).toMatchObject({ status: 409, code: 'sync_in_progress' });
  });

  it('unlinks an account: it leaves the list at once and the library is stale', async () => {
    client.setQueryData(linkedAccountKeys.list, [ACCOUNT, JUST_LINKED_ELSEWHERE]);
    client.setQueryData(libraryKeys.stats, TEST_LIBRARY_STATS);
    const unlink = new MutationObserver(client, queries.unlink());

    const done = unlink.mutate(ACCOUNT.id);
    const sent = await request(`/api/v1/linked-accounts/${ACCOUNT.id}`);
    expect(sent.request.method).toBe('DELETE');
    sent.flush(null, { status: 204, statusText: '' });
    await done;

    expect(client.getQueryData(linkedAccountKeys.list)).toEqual([JUST_LINKED_ELSEWHERE]);
    expect(stale(linkedAccountKeys.list)).toBe(true);
    expect(stale(libraryKeys.stats)).toBe(true);
  });

  it('keeps the account in the list when unlinking is refused', async () => {
    client.setQueryData(linkedAccountKeys.list, [ACCOUNT]);
    const unlink = new MutationObserver(client, queries.unlink());

    const done = unlink.mutate(ACCOUNT.id).catch((thrown: unknown) => thrown);
    (await request(`/api/v1/linked-accounts/${ACCOUNT.id}`)).flush(
      { error: { code: 'database_unavailable', message: 'In English, for the log.' } },
      { status: 503, statusText: '' },
    );

    expect(await done).toMatchObject({ status: 503, code: 'database_unavailable' });
    expect(client.getQueryData(linkedAccountKeys.list)).toEqual([ACCOUNT]);
  });

  it('gives an account its owner’s key, then asks again for the accounts', async () => {
    client.setQueryData(linkedAccountKeys.list, [ACCOUNT]);
    const give = new MutationObserver(client, queries.giveKey());

    const done = give.mutate({ accountId: ACCOUNT.id, key: 'A1B2' });
    const sent = await request(`/api/v1/linked-accounts/${ACCOUNT.id}/key`);
    expect(sent.request.method).toBe('PUT');
    expect(sent.request.body).toEqual({ key: 'A1B2' });
    sent.flush(null, { status: 204, statusText: '' });
    await done;

    expect(stale(linkedAccountKeys.list)).toBe(true);
  });

  it('takes the owner’s key away, then asks again for the accounts', async () => {
    client.setQueryData(linkedAccountKeys.list, [{ ...ACCOUNT, has_own_key: true }]);
    const remove = new MutationObserver(client, queries.removeKey());

    const done = remove.mutate(ACCOUNT.id);
    const sent = await request(`/api/v1/linked-accounts/${ACCOUNT.id}/key`);
    expect(sent.request.method).toBe('DELETE');
    sent.flush(null, { status: 204, statusText: '' });
    await done;

    expect(stale(linkedAccountKeys.list)).toBe(true);
  });
});

describe('the Steam link', () => {
  it('starts at the API and comes back to a path of the app', () => {
    expect(steamLinkAddress('/library?view=list')).toBe(
      '/api/v1/linked-accounts/steam/link?return_to=%2Flibrary%3Fview%3Dlist',
    );
  });

  it('reads the outcome an address carries', () => {
    expect(steamLinkOutcome(null)).toBeNull();
    expect(steamLinkOutcome('linked')).toBe('linked');
    expect(steamLinkOutcome('linked_elsewhere')).toBe('linked_elsewhere');
  });

  it('takes a word it does not know as a failure', () => {
    expect(steamLinkOutcome('something_new')).toBe('failed');
  });
});
