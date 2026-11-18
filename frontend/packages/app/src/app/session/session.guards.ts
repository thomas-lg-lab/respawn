import { inject } from '@angular/core';
import { type CanActivateFn, Router } from '@angular/router';
import { type Account, AccountQueries } from '@respawn/data';
import { QueryClient } from '@tanstack/angular-query';

/**
 * Who is signed in, from the cache when it knows, else asked once. A request that fails
 * (the database away, no network) counts as nobody here: the sign-in screen is then the one
 * that says what is wrong and offers to try again, which asks this again too.
 */
async function currentAccount(
  client: QueryClient,
  queries: AccountQueries,
): Promise<Account | null> {
  try {
    return await client.query(queries.me());
  } catch {
    return null;
  }
}

/** The screens of the app: without a session, the way in is the sign-in screen. */
export const signedInOnly: CanActivateFn = async () => {
  const router = inject(Router);
  const account = await currentAccount(inject(QueryClient), inject(AccountQueries));
  return account ? true : router.parseUrl('/sign-in');
};

/** The sign-in screen: with a session there is nothing to do there. */
export const signedOutOnly: CanActivateFn = async () => {
  const router = inject(Router);
  const account = await currentAccount(inject(QueryClient), inject(AccountQueries));
  return account ? router.parseUrl('/') : true;
};
