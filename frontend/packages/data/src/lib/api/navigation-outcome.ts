/**
 * The outcome an address carries, if any, among the ones the front knows: what the API wrote
 * on the address it sent the browser back to, after a navigation through another site. A
 * word the front does not know (a later version of the API, or one written by hand) reads as
 * a failure: something was said, and it was not a success.
 */
export function outcomeAmong<T extends string>(
  known: readonly T[],
  value: string | null | undefined,
): T | 'failed' | null {
  if (!value) return null;
  return known.find((each) => each === value) ?? 'failed';
}
