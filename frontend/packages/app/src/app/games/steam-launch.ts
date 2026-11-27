import { type Signal, computed, inject } from '@angular/core';
import { DomSanitizer, type SafeUrl } from '@angular/platform-browser';
import type { LibraryGame } from '@respawn/data';
import { steamLaunchAddress } from './game-list';

/**
 * The link that launches a game in the Steam client, for the component that shows it; none
 * when no copy of the game is listed there. Angular takes any address that is not the web's
 * for a dangerous one and would write it off; this one is trusted because
 * `steamLaunchAddress` only ever gives `steam://run/` followed by digits.
 */
export function steamLaunchLink(
  game: Signal<Pick<LibraryGame, 'entries'>>,
): Signal<SafeUrl | null> {
  const sanitizer = inject(DomSanitizer);
  return computed(() => {
    const address = steamLaunchAddress(game());
    return address === null ? null : sanitizer.bypassSecurityTrustUrl(address);
  });
}
