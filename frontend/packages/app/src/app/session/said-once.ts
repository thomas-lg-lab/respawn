import type { Location } from '@angular/common';
import type { Router } from '@angular/router';

/**
 * Takes off the browser's address a word the API wrote on it for one arrival (the outcome of a
 * navigation through another site): the screen has read it and says it, and a reload, a
 * bookmark or a copy of the address must not say it again.
 *
 * Only the address the browser shows changes, in place: no navigation happens, so the screen
 * keeps what it read until the visitor moves on.
 */
export function saidOnce(location: Location, router: Router, parameter: string): void {
  const here = router.parseUrl(location.path(true));
  if (!(parameter in here.queryParams)) return;

  here.queryParams = Object.fromEntries(
    Object.entries(here.queryParams).filter(([key]) => key !== parameter),
  );
  location.replaceState(router.serializeUrl(here), '', location.getState());
}
