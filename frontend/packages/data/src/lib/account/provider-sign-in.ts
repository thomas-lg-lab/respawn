import { API_BASE } from '../api/api';
import { outcomeAmong } from '../api/navigation-outcome';

/**
 * Signing in through a provider is not a call but a navigation: the browser goes to the API,
 * which sends it to the provider and, once the visitor signed in there, back to the address
 * the front gave: a session is opened, or, from the link route, the provider is linked to
 * the signed-in account. No token of the provider ever reaches the front.
 */

/** The parameter the API writes on the address it sends the browser back to, with the outcome. */
export const PROVIDER_OUTCOME = 'provider_sign_in';

/** What the API says came of a sign-in or a link through a provider, other than a session. */
export const PROVIDER_OUTCOMES = [
  'linked',
  'registration_closed',
  'link_provider_first',
  'linked_elsewhere',
  'denied',
  'failed',
  'unavailable',
] as const;

export type ProviderOutcome = (typeof PROVIDER_OUTCOMES)[number];

/**
 * The address that starts a sign-in through `provider` and comes back to `returnTo`, a path of
 * the app. With `link`, it links the provider to the signed-in account instead: a route of its
 * own, so that a sign-in is never taken for a link.
 */
export function providerSignInAddress(provider: string, returnTo: string, link = false): string {
  const route = `${API_BASE}/auth/oidc/${encodeURIComponent(provider)}${link ? '/link' : ''}`;
  return `${route}?return_to=${encodeURIComponent(returnTo)}`;
}

/** The outcome an address carries, if any; a word the front does not know reads as a failure. */
export function providerOutcome(value: string | null | undefined): ProviderOutcome | null {
  return outcomeAmong(PROVIDER_OUTCOMES, value);
}
