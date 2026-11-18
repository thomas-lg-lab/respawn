import { inject } from '@angular/core';
import { type CanActivateFn, RedirectCommand, Router } from '@angular/router';
import { type Account, AccountQueries } from '@respawn/data';
import { QueryClient } from '@tanstack/angular-query';
import { RETURN_TO, returnAddress, signInFrom } from './return-address';

/**
 * The API could not say who is signed in (its database away, no network). Not an answer: it
 * is neither an account nor "nobody".
 */
const UNKNOWN = Symbol('account unknown');

/**
 * Who is signed in, from the cache when it knows, else asked once.
 *
 * An account the cache holds stays the answer, even when asking again has failed since (the
 * window came back in front with no network): only the API saying so ends a session, and
 * the server state then empties this.
 */
async function currentAccount(
  client: QueryClient,
  queries: AccountQueries,
): Promise<Account | null | typeof UNKNOWN> {
  const known = client.getQueryData(queries.me().queryKey);
  if (known) return known;
  try {
    return await client.query(queries.me());
  } catch {
    return UNKNOWN;
  }
}

/**
 * The screens of the app: without a session, the way in is the sign-in screen, which is told
 * the address that was asked for.
 *
 * When the API could not say who is there, the sign-in is the screen that says what is wrong
 * and offers to try again. Its guard is told so, rather than left to ask the same question
 * at once and make the visitor wait twice for the same failure.
 */
export const signedInOnly: CanActivateFn = async (_route, state) => {
  const router = inject(Router);
  const account = await currentAccount(inject(QueryClient), inject(AccountQueries));
  if (account === UNKNOWN) {
    return new RedirectCommand(signInFrom(router, state.url), { info: UNKNOWN });
  }
  return account ? true : signInFrom(router, state.url);
};

/**
 * The sign-in screen: with a session there is nothing to do there, and the visitor goes on to
 * where they were headed.
 */
export const signedOutOnly: CanActivateFn = async (route) => {
  const router = inject(Router);
  if (router.currentNavigation()?.extras.info === UNKNOWN) return true;

  const account = await currentAccount(inject(QueryClient), inject(AccountQueries));
  return account && account !== UNKNOWN
    ? router.parseUrl(returnAddress(route.queryParamMap.get(RETURN_TO)))
    : true;
};
