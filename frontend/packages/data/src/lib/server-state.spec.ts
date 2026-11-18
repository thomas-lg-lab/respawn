import { MutationObserver } from '@tanstack/angular-query';
import { accountKeys } from './account/account.queries';
import { ApiFailure } from './api/api';
import { serverState } from './server-state';
import { TEST_ACCOUNT as ACCOUNT } from './testing/fixtures';

const refusal = (status: number, code: string) =>
  Promise.reject(new ApiFailure(status, code, 'In English, for the log.'));

describe('serverState', () => {
  const ended = vi.fn();

  beforeEach(() => ended.mockClear());

  /** The state of someone signed in, who has read their library. */
  function signedIn() {
    const client = serverState(ended);
    client.setQueryData(accountKeys.me, ACCOUNT);
    client.setQueryData(['library'], { total: 3 });
    return client;
  }

  it('closes the session when a read is refused for want of one', async () => {
    const client = signedIn();

    await client
      .fetchQuery({ queryKey: ['games'], queryFn: () => refusal(401, 'not_signed_in') })
      .catch(() => undefined);

    expect(client.getQueryData(accountKeys.me)).toBeNull();
    // Nothing read for the account that was there stays behind for the next one.
    expect(client.getQueryData(['library'])).toBeUndefined();
    expect(ended).toHaveBeenCalledOnce();
  });

  it('closes the session when the account itself is refused, and ends up with nobody', async () => {
    const client = signedIn();

    await client
      .fetchQuery({ queryKey: accountKeys.me, queryFn: () => refusal(401, 'not_signed_in') })
      .catch(() => undefined);

    expect(client.getQueryState(accountKeys.me)).toMatchObject({ status: 'success', data: null });
    expect(ended).toHaveBeenCalledOnce();
  });

  it('closes the session when an action is refused for want of one', async () => {
    const client = signedIn();
    const action = new MutationObserver(client, {
      mutationFn: () => refusal(401, 'not_signed_in'),
    });

    await action.mutate().catch(() => undefined);

    expect(client.getQueryData(accountKeys.me)).toBeNull();
    expect(client.getQueryData(['library'])).toBeUndefined();
  });

  // Several requests were on their way when the session ended: each is refused in turn.
  it('closes the session once, and leaves alone what was asked after it', async () => {
    const client = signedIn();
    const late = (key: string) =>
      client
        .fetchQuery({ queryKey: [key], queryFn: () => refusal(401, 'not_signed_in') })
        .catch(() => undefined);

    await late('games');
    // The sign-in screen has asked what the instance offers by the time the second arrives.
    client.setQueryData(accountKeys.signInOptions, { local_sign_in: true });
    await late('friends');

    expect(ended).toHaveBeenCalledOnce();
    expect(client.getQueryData(accountKeys.signInOptions)).toEqual({ local_sign_in: true });
  });

  it('closes the session on a 401 that is not the API’s: something in front refused', async () => {
    const client = signedIn();

    await client
      .fetchQuery({ queryKey: ['games'], queryFn: () => refusal(401, 'http_error') })
      .catch(() => undefined);

    expect(client.getQueryData(accountKeys.me)).toBeNull();
    expect(ended).toHaveBeenCalledOnce();
  });

  it('keeps the session through any other refusal', async () => {
    const client = signedIn();

    await client
      .fetchQuery({ queryKey: ['games'], queryFn: () => refusal(503, 'database_unavailable') })
      .catch(() => undefined);
    // Wrong credentials are a 401 too, and close nothing.
    await client
      .fetchQuery({ queryKey: ['again'], queryFn: () => refusal(401, 'invalid_credentials') })
      .catch(() => undefined);

    expect(client.getQueryData(accountKeys.me)).toEqual(ACCOUNT);
    expect(client.getQueryData(['library'])).toEqual({ total: 3 });
    expect(ended).not.toHaveBeenCalled();
  });
});
