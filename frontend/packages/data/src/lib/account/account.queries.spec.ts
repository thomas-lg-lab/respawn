import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MutationObserver, QueryClient, provideTanStackQuery } from '@tanstack/angular-query';
import { ApiFailure, NETWORK_ERROR } from '../api/api';
import type { SignInOptions } from '../models/models';
import { TEST_ACCOUNT as ACCOUNT } from '../testing/fixtures';
import { AccountQueries, accountKeys } from './account.queries';

const OPTIONS: SignInOptions = {
  local_sign_in: true,
  registration_open: true,
  first_account: true,
  password_min_length: 12,
  oidc_providers: [],
};

describe('AccountQueries', () => {
  let queries: AccountQueries;
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
    queries = TestBed.inject(AccountQueries);
    client = TestBed.inject(QueryClient);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** The request once it left: a query starts on a later tick. */
  const request = (url: string) => vi.waitFor(() => http.expectOne(url));

  const refuse = (code: string, status: number) =>
    [{ error: { code, message: 'In English, for the log.' } }, { status, statusText: '' }] as const;

  it('reads the signed-in account', async () => {
    const me = client.fetchQuery(queries.me());
    (await request('/api/v1/me')).flush(ACCOUNT);

    expect(await me).toEqual(ACCOUNT);
  });

  it('takes "not signed in" as an answer, not as a failure', async () => {
    const me = client.fetchQuery(queries.me());
    (await request('/api/v1/me')).flush(...refuse('not_signed_in', 401));

    expect(await me).toBeNull();
  });

  it('takes a 401 that is not the API’s as "not signed in" too', async () => {
    const me = client.fetchQuery(queries.me());
    (await request('/api/v1/me')).flush('<html>Sign in</html>', { status: 401, statusText: '' });

    expect(await me).toBeNull();
  });

  // The server state closes the session on that refusal, as on any other request's.
  it('takes "not signed in" as a refusal once it knew an account: the session ended', async () => {
    client.setQueryData(accountKeys.me, ACCOUNT);
    const me = client.fetchQuery({ ...queries.me(), staleTime: 0 });
    (await request('/api/v1/me')).flush(...refuse('not_signed_in', 401));

    expect(await me.catch((thrown: unknown) => thrown)).toMatchObject({ code: 'not_signed_in' });
  });

  it('fails with the code of the API when it cannot say who is signed in', async () => {
    const me = client.fetchQuery(queries.me());
    (await request('/api/v1/me')).flush(...refuse('database_unavailable', 503));

    const failure = await me.catch((thrown: unknown) => thrown);
    expect(failure).toBeInstanceOf(ApiFailure);
    expect(failure).toMatchObject({ status: 503, code: 'database_unavailable' });
  });

  it('names a request that got no answer', async () => {
    const options = client.fetchQuery(queries.signInOptions());
    (await request('/api/v1/config')).error(new ProgressEvent('error'));

    expect(await options.catch((thrown: unknown) => thrown)).toMatchObject({
      status: 0,
      code: NETWORK_ERROR,
    });
  });

  it('names an answer that is not in the format of the API', async () => {
    const options = client.fetchQuery(queries.signInOptions());
    (await request('/api/v1/config')).flush('<html>Bad gateway</html>', {
      status: 502,
      statusText: '',
    });

    expect(await options.catch((thrown: unknown) => thrown)).toMatchObject({
      status: 502,
      code: 'http_error',
    });
  });

  it('knows who is signed in as soon as a sign-in succeeds', async () => {
    client.setQueryData(accountKeys.signInOptions, OPTIONS);
    const signIn = new MutationObserver(client, queries.signIn());

    const done = signIn.mutate({ email: 'first@example.org', password: 'correct horse battery' });
    const sent = await request('/api/v1/auth/login');
    expect(sent.request.body).toEqual({
      email: 'first@example.org',
      password: 'correct horse battery',
    });
    sent.flush(ACCOUNT);
    await done;

    expect(client.getQueryData(accountKeys.me)).toEqual(ACCOUNT);
    // What the sign-in page offered is not kept for a later visit.
    expect(client.getQueryData(accountKeys.signInOptions)).toBeUndefined();
  });

  it('creates an account, which is signed in at once', async () => {
    const register = new MutationObserver(client, queries.register());

    const done = register.mutate({
      email: 'first@example.org',
      display_name: 'First',
      password: 'correct horse battery',
    });
    (await request('/api/v1/auth/register')).flush(ACCOUNT, { status: 201, statusText: '' });
    await done;

    expect(client.getQueryData(accountKeys.me)).toEqual(ACCOUNT);
  });

  it('asks again what the page may offer when a registration is refused as closed', async () => {
    client.setQueryData(accountKeys.signInOptions, OPTIONS);
    const register = new MutationObserver(client, queries.register());

    const done = register
      .mutate({
        email: 'second@example.org',
        display_name: 'Second',
        password: 'correct horse battery',
      })
      .catch((thrown: unknown) => thrown);
    (await request('/api/v1/auth/register')).flush(...refuse('registration_closed', 403));

    expect(await done).toMatchObject({ code: 'registration_closed' });
    expect(client.getQueryState(accountKeys.signInOptions)?.isInvalidated).toBe(true);
  });

  it('leaves nothing of the account behind after a sign-out', async () => {
    client.setQueryData(accountKeys.me, ACCOUNT);
    client.setQueryData(['library'], { total: 3 });
    const signOut = new MutationObserver(client, queries.signOut());

    const done = signOut.mutate();
    (await request('/api/v1/auth/logout')).flush(null, { status: 204, statusText: '' });
    await done;

    expect(client.getQueryData(accountKeys.me)).toBeNull();
    expect(client.getQueryData(['library'])).toBeUndefined();
  });

  it('stays signed in when the sign-out is refused', async () => {
    client.setQueryData(accountKeys.me, ACCOUNT);
    const signOut = new MutationObserver(client, queries.signOut());

    const done = signOut.mutate().catch((thrown: unknown) => thrown);
    (await request('/api/v1/auth/logout')).flush(...refuse('database_unavailable', 503));

    expect(await done).toMatchObject({ code: 'database_unavailable' });
    expect(client.getQueryData(accountKeys.me)).toEqual(ACCOUNT);
  });

  it('keeps the three settings on the account, and the account as it answered', async () => {
    client.setQueryData(accountKeys.me, ACCOUNT);
    const save = new MutationObserver(client, queries.saveSettings());
    const settings = { language: 'fr', theme: 'dark', character: 'plain' };

    const done = save.mutate(settings);
    const sent = await request('/api/v1/me/settings');
    expect(sent.request.method).toBe('PUT');
    expect(sent.request.body).toEqual(settings);
    sent.flush({ ...ACCOUNT, settings });
    await done;

    expect(client.getQueryData(accountKeys.me)).toEqual({ ...ACCOUNT, settings });
  });
});
