import type { Router, UrlTree } from '@angular/router';

/** The parameter of the sign-in's address that names where to go once signed in. */
export const RETURN_TO = 'returnTo';

const HOME = '/';
const SIGN_IN = '/sign-in';

/**
 * Where to go once signed in: the address that was asked for, when it is one of the app's
 * own, else the home.
 *
 * The address comes from the sign-in's own address, which anyone can write into a link. So
 * it is only ever a path of this origin: it starts with one `/`, and holds neither a
 * backslash nor a control character, which a browser could read as the start of another
 * host. The sign-in itself is no place to come back to.
 */
export function returnAddress(asked: string | null | undefined): string {
  if (!asked?.startsWith('/') || asked.startsWith('//')) return HOME;
  if ([...asked].some((character) => character === '\\' || character < ' ')) return HOME;

  const path = asked.split(/[?#]/, 1)[0];
  return path === SIGN_IN || path?.startsWith(SIGN_IN + '/') ? HOME : asked;
}

/** The sign-in, told where the visitor was. Nothing is said for the home: it is where a sign-in leads anyway. */
export function signInFrom(router: Router, address: string): UrlTree {
  const back = returnAddress(address);
  return router.createUrlTree([SIGN_IN], {
    queryParams: back === HOME ? {} : { [RETURN_TO]: back },
  });
}
