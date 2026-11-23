import { ApiFailure, NETWORK_ERROR } from '@respawn/data';

/**
 * The refusals a screen has its own sentence for, by the API's code. The API's English
 * message is never shown: the interface speaks the reader's language.
 */
export const KNOWN_FAILURES = [
  'invalid_credentials',
  'too_many_requests',
  'registration_closed',
  'local_sign_in_disabled',
  'account_exists',
  'invalid_email',
  'weak_password',
  'invalid_request',
  'cross_site_request',
  'sync_in_progress',
  'unknown_linked_account',
  'invalid_key',
  'keys_unavailable',
  'database_unavailable',
  'internal_error',
  NETWORK_ERROR,
] as const;

/** The translation key of what went wrong; anything unforeseen gets the general sentence. */
export function failureKey(failure: unknown): string {
  const code = failure instanceof ApiFailure ? failure.code : null;
  return KNOWN_FAILURES.some((known) => known === code) ? `errors.${code}` : 'errors.unknown';
}
