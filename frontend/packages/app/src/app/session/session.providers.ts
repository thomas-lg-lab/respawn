import { type EnvironmentProviders, inject } from '@angular/core';
import { Router } from '@angular/router';
import { serverState } from '@respawn/data';
import { provideTanStackQuery } from '@tanstack/angular-query';

/**
 * The server state, with the way out: when the API says the session ended, whatever screen
 * asked, the sign-in is where the app goes.
 */
export function provideServerState(): EnvironmentProviders {
  return provideTanStackQuery(() => {
    const router = inject(Router);
    return serverState(() => void router.navigateByUrl('/sign-in'));
  });
}
