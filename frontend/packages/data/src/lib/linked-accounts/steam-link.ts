import { API_BASE } from '../api/api';

/**
 * Linking a Steam account is not a call but a navigation: the browser goes to the API, which
 * sends it to Steam and, once the visitor signed in there, back to the address the front
 * gave. It links a library and never opens a session.
 */

/** The parameter the API writes on the address it sends the browser back to, with the outcome. */
export const STEAM_LINK_OUTCOME = 'steam_link';

/** What the API says came of linking a Steam account. */
export const STEAM_LINK_OUTCOMES = ['linked', 'linked_elsewhere', 'failed', 'unavailable'] as const;

export type SteamLinkOutcome = (typeof STEAM_LINK_OUTCOMES)[number];

/** The address that starts the link and comes back to `returnTo`, a path of the app. */
export function steamLinkAddress(returnTo: string): string {
  return `${API_BASE}/linked-accounts/steam/link?return_to=${encodeURIComponent(returnTo)}`;
}

/**
 * The outcome an address carries, if any. A word the front does not know (a later version of
 * the API, or one written by hand) reads as a failure: something was said, and it was not a
 * success.
 */
export function steamLinkOutcome(value: string | null | undefined): SteamLinkOutcome | null {
  if (!value) return null;
  return STEAM_LINK_OUTCOMES.find((known) => known === value) ?? 'failed';
}
