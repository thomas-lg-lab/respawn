import { type EnvironmentProviders, inject } from '@angular/core';
import { Router } from '@angular/router';
import { serverState } from '@respawn/data';
import { provideTanStackQuery } from '@tanstack/angular-query';
import { signInFrom } from './return-address';
import { withoutSaidOnce } from './said-once';

/**
 * The server state, with the way out: when the API says the session ended, whatever screen
 * asked, the sign-in is where the app goes, told the address the visitor was on, without what
 * that address said for one arrival.
 */
export function provideServerState(): EnvironmentProviders {
  return provideTanStackQuery(() => {
    const router = inject(Router);
    return serverState(
      () => void router.navigateByUrl(signInFrom(router, withoutSaidOnce(router, router.url))),
    );
  });
}
