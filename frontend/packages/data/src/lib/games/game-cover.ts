import { API_BASE } from '../api/api';

/**
 * A game's cover is not a call but an image the browser asks for itself, with the session's
 * cookie: the API answers the artwork, or 404 when the game has none. With a source, the one
 * the account chose: its own address, so that a browser never keeps one in the place of the other.
 */
export function coverAddress(gameId: string, source?: string | null): string {
  const address = `${API_BASE}/games/${encodeURIComponent(gameId)}/cover`;
  return source ? `${address}?source=${encodeURIComponent(source)}` : address;
}
