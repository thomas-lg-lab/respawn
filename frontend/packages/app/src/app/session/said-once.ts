import type { Location } from '@angular/common';
import type { Router } from '@angular/router';
import { PROVIDER_OUTCOME, STEAM_LINK_OUTCOME } from '@respawn/data';

/** The title the home asks the Library to look for, written on the address that leads there. */
export const LIBRARY_SEARCH = 'q';

/**
 * Every word an address carries for one arrival: what came of a navigation through another
 * site, or what one screen asks of the next.
 */
const SAID_ONCE: readonly string[] = [PROVIDER_OUTCOME, STEAM_LINK_OUTCOME, LIBRARY_SEARCH];

/**
 * Takes off the browser's address a word written on it for one arrival (the outcome of a
 * navigation through another site, a search another screen asked for): the screen has read it
 * and says it, and a reload, a bookmark or a copy of the address must not say it again.
 *
 * Only the address the browser shows changes, in place: no navigation happens, so the screen
 * keeps what it read until the visitor moves on. The router still holds the word: an address
 * to come back to is made with `withoutSaidOnce`.
 */
export function saidOnce(location: Location, router: Router, parameter: string): void {
  const here = router.parseUrl(location.path(true));
  if (!(parameter in here.queryParams)) return;

  here.queryParams = Object.fromEntries(
    Object.entries(here.queryParams).filter(([key]) => key !== parameter),
  );
  location.replaceState(router.serializeUrl(here), '', location.getState());
}

/**
 * An address of the app without the words said once: the one to come back to, after a
 * sign-in or a link through a provider, since the router's own still holds them.
 */
export function withoutSaidOnce(router: Router, address: string): string {
  const there = router.parseUrl(address);
  there.queryParams = Object.fromEntries(
    Object.entries(there.queryParams).filter(([key]) => !SAID_ONCE.includes(key)),
  );
  return router.serializeUrl(there);
}
