/** The rules a form of this app declares, by the `kind` of the error each one gives. */
const KNOWN_KINDS = ['required', 'email', 'minLength', 'maxLength'];

/**
 * The translation key of why a field is refused: its first error, since one reason at a
 * time is enough to correct it. `null` when the field has none.
 */
export function fieldErrorKey(errors: readonly { readonly kind: string }[]): string | null {
  const [first] = errors;
  if (!first) return null;
  return `fields.errors.${KNOWN_KINDS.includes(first.kind) ? first.kind : 'invalid'}`;
}
