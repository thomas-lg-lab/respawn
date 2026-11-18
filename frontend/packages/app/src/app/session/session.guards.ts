import { inject } from '@angular/core';
import { type CanActivateFn, Router } from '@angular/router';
import { type Account, AccountQueries } from '@respawn/data';
import { QueryClient } from '@tanstack/angular-query';
import { RETURN_TO, returnAddress, signInFrom } from './return-address';

/**
 * Who is signed in, from the cache when it knows, else asked once. A request that fails
 * (the database away, no network) counts as nobody here: the sign-in screen is then the one
 * that says what is wrong and offers to try again, which asks this again too.
 *
 * An account the cache holds stays the answer, even when asking again has failed since (the
 * window came back in front with no network): only the API saying so ends a session, and
 * the server state then empties this.
 */
async function currentAccount(
  client: QueryClient,
  queries: AccountQueries,
): Promise<Account | null> {
  const known = client.getQueryData(queries.me().queryKey);
  if (known) return known;
  try {
    return await client.query(queries.me());
  } catch {
    return null;
  }
}

/**
 * The screens of the app: without a session, the way in is the sign-in screen, which is told
 * the address that was asked for.
 */
export const signedInOnly: CanActivateFn = async (_route, state) => {
  const router = inject(Router);
  const account = await currentAccount(inject(QueryClient), inject(AccountQueries));
  return account ? true : signInFrom(router, state.url);
};

/**
 * The sign-in screen: with a session there is nothing to do there, and the visitor goes on to
 * where they were headed.
 */
export const signedOutOnly: CanActivateFn = async (route) => {
  const router = inject(Router);
  const account = await currentAccount(inject(QueryClient), inject(AccountQueries));
  return account ? router.parseUrl(returnAddress(route.queryParamMap.get(RETURN_TO))) : true;
};
