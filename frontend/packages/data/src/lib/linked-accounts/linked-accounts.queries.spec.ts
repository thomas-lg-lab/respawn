import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MutationObserver, QueryClient, provideTanStackQuery } from '@tanstack/angular-query';
import { libraryKeys } from '../library/library.queries';
import type { LinkedAccount } from '../models/models';
import {
  TEST_LINKED_ACCOUNT as ACCOUNT,
  TEST_LIBRARY_STATS,
  TEST_SYNC_RUN,
} from '../testing/fixtures';
import {
  AWAITING_SYNC_INTERVAL,
  LinkedAccountQueries,
  awaitsSync,
  linkedAccountKeys,
} from './linked-accounts.queries';
import { steamLinkAddress, steamLinkOutcome } from './steam-link';

const JUST_LINKED: LinkedAccount = {
  ...ACCOUNT,
  sync: { running: false, last: null, last_succeeded_at: null },
};

describe('LinkedAccountQueries', () => {
  let queries: LinkedAccountQueries;
  let client: QueryClient;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTanStackQuery(() => new QueryClient()),
      ],
    });
    queries = TestBed.inject(LinkedAccountQueries);
    client = TestBed.inject(QueryClient);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** The request once it left: a query starts on a later tick. */
  const request = (url: string) => vi.waitFor(() => http.expectOne(url));

  const stale = (key: readonly string[]) => client.getQueryState(key)?.isInvalidated;

  it('reads the linked accounts', async () => {
    const accounts = client.fetchQuery(queries.list());
    (await request('/api/v1/linked-accounts')).flush({ accounts: [ACCOUNT] });

    expect(await accounts).toEqual([ACCOUNT]);
  });

  it('awaits a sync that runs, and the first one of an account just linked', () => {
    expect(awaitsSync(ACCOUNT)).toBe(false);
    expect(awaitsSync(JUST_LINKED)).toBe(true);
    expect(awaitsSync({ ...ACCOUNT, sync: { ...ACCOUNT.sync, running: true } })).toBe(true);
  });

  it('asks again every few seconds only while a sync is awaited', () => {
    const interval = queries.list().refetchInterval as (query: {
      state: { data?: LinkedAccount[] };
    }) => number | false;

    expect(interval({ state: { data: [JUST_LINKED] } })).toBe(AWAITING_SYNC_INTERVAL);
    expect(interval({ state: { data: [ACCOUNT] } })).toBe(false);
    expect(interval({ state: {} })).toBe(false);
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
