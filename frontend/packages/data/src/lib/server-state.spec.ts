import { MutationObserver } from '@tanstack/angular-query';
import { accountKeys } from './account/account.queries';
import { ApiFailure } from './api/api';
import type { Account } from './models/models';
import { serverState } from './server-state';

const ACCOUNT: Account = {
  id: '0b0f6f3e-6a59-4c8c-9a55-2d7f3f0d6c11',
  email: 'first@example.org',
  display_name: 'First',
  is_admin: true,
};

const refusal = (status: number, code: string) =>
  Promise.reject(new ApiFailure(status, code, 'In English, for the log.'));

describe('serverState', () => {
  /** The state of someone signed in, who has read their library. */
  function signedIn() {
    const client = serverState();
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
  });
});
