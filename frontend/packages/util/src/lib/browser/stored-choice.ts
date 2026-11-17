/**
 * A choice kept in this browser: what holds the settings before sign-in. Reads give back
 * only a value still on the list, so a key written by an older version, or by hand, falls
 * back to the default instead of reaching the page.
 */
export function readChoice<T extends string>(key: string, allowed: readonly T[]): T | null {
  try {
    const stored = localStorage.getItem(key);
    return allowed.find((value) => value === stored) ?? null;
  } catch {
    // Storage refused (private browsing, a locked-down profile): the default applies.
    return null;
  }
}

/** Keeps the choice, or forgets it when it is `null`. Best effort: the page already shows it. */
export function writeChoice(key: string, value: string | null): void {
  try {
    if (value === null) {
      localStorage.removeItem(key);
    } else {
      localStorage.setItem(key, value);
    }
  } catch {
    // Nothing to do: the choice still holds until the page is closed.
  }
}
