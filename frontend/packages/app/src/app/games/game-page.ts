/**
 * The way to a game's page, from wherever it is reached: a row of the library, a block of the
 * home, a tile of the time map, a suggestion of the header's search. The screen it was reached
 * from rides with the navigation (`history.state`), so the page's back link can name it and
 * lead back to it as it was left.
 */

/** The screens the page of a game leads back to, by the address of each. */
export const ORIGINS = { home: '/', library: '/library', time: '/time' } as const;
export type Origin = keyof typeof ORIGINS;

/** What a link to a game's page carries in the navigation's state. */
export interface GamePageState {
  readonly from: Origin;
}

/** The address of a game's page, as `routerLink` takes it. */
export function gamePage(gameId: string): string[] {
  return ['/games', gameId];
}

/** The state a link from `from` gives the navigation to a game's page. */
export function cameFrom(from: Origin): GamePageState {
  return { from };
}

/** The screen an address is on, if it is one a game's page leads back to. */
export function originOf(path: string): Origin | null {
  const bare = path.split(/[?#]/)[0];
  const found = Object.entries(ORIGINS).find(([, address]) => address === bare);
  return found ? (found[0] as Origin) : null;
}

/** The screen a navigation's state says it came from, if it says one this page knows. */
export function originIn(state: unknown): Origin | null {
  const from = (state as Partial<GamePageState> | null)?.from;
  return typeof from === 'string' && Object.hasOwn(ORIGINS, from) ? from : null;
}
