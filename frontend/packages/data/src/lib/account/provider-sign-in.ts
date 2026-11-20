import { API_BASE } from '../api/api';

/**
 * Signing in through a provider is not a call but a navigation: the browser goes to the API,
 * which sends it to the provider and, once the visitor signed in there, back to the address
 * the front gave. Without a session that opens one; with a session, the provider is linked
 * to the signed-in account instead. No token of the provider ever reaches the front.
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

/** The address that starts a sign-in through `provider` and comes back to `returnTo`, a path of the app. */
export function providerSignInAddress(provider: string, returnTo: string): string {
  return `${API_BASE}/auth/oidc/${encodeURIComponent(provider)}?return_to=${encodeURIComponent(returnTo)}`;
}

/**
 * The outcome an address carries, if any. A word the front does not know (a later version of
 * the API, or one written by hand) reads as a failure: something was said, and it was not a
 * success.
 */
export function providerOutcome(value: string | null | undefined): ProviderOutcome | null {
  if (!value) return null;
  return PROVIDER_OUTCOMES.find((known) => known === value) ?? 'failed';
}
