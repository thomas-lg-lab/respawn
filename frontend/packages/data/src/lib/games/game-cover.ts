import { API_BASE } from '../api/api';

/**
 * A game's cover is not a call but an image the browser asks for itself, with the session's
 * cookie: the API answers the artwork, or 404 when the game has none.
 */
export function coverAddress(gameId: string): string {
  return `${API_BASE}/games/${encodeURIComponent(gameId)}/cover`;
}
