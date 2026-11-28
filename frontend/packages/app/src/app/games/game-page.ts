/**
 * The way to a game's page, from wherever it is reached: a row of the library, a block of the
 * home, a tile of the time map, a suggestion of the header's search. The screen it was reached
 * from rides with the navigation (`history.state`), so the page's back link can name it and
 * lead back to it as it was left, and the header can light its tab. A game opened from another
 * game's page keeps that page's screen, and says it came through a game: the browser's way
 * back would return to that game, not to the screen.
 */

/** The screens the page of a game leads back to, by the address of each. */
export const ORIGINS = { home: '/', library: '/library', time: '/time' } as const;
export type Origin = keyof typeof ORIGINS;

/**
 * The screen a game's page opened from its address alone (a pasted link, a reload) leads back
 * to, and stands under in the header's tabs.
 */
export const UNSAID_ORIGIN: Origin = 'library';

/** What a link to a game's page carries in the navigation's state. */
export interface GamePageState {
  readonly from: Origin;
  /** Reached from another game's page, which itself came from `from`. */
  readonly throughGame?: true;
}

/** Where a game's page stands, as its navigation's state says it. */
export interface GamePageOrigin {
  /** The screen its back link names and leads to, and whose tab the header lights. */
  readonly screen: Origin;
  /** Whether the browser's way back returns to that screen as it was left. */
  readonly returns: boolean;
}

/** The address of a game's page, as `routerLink` takes it. */
export function gamePage(gameId: string): string[] {
  return ['/games', gameId];
}

/** An address without its matrix parameters, its query and its fragment. */
function bare(path: string): string {
  return path.split(/[;?#]/, 1)[0] ?? path;
}

/** Whether an address is a game's page. */
export function isGamePage(path: string): boolean {
  return /^\/games\/[^/]+$/.test(bare(path));
}

/** The state a link from the screen `from` gives the navigation to a game's page. */
export function cameFrom(from: Origin): GamePageState {
  return { from };
}

/** The state a link on a game's page that stands under `from` gives the next game's page. */
export function cameThroughGame(from: Origin): GamePageState {
  return { from, throughGame: true };
}

/** The screen an address is on, if it is one a game's page leads back to. */
export function originOf(path: string): Origin | null {
  const address = bare(path);
  const found = Object.entries(ORIGINS).find(([, each]) => each === address);
  return found ? (found[0] as Origin) : null;
}

/** The screen a navigation's state says it came from, if it says one this page knows. */
export function originIn(state: unknown): Origin | null {
  const from = (state as Partial<GamePageState> | null)?.from;
  return typeof from === 'string' && Object.hasOwn(ORIGINS, from) ? from : null;
}

/**
 * Where a game's page reached with this navigation's state stands: the screen it says, or the
 * library for a page opened from its address alone, which the browser's way back does not reach.
 */
export function gamePageOrigin(state: unknown): GamePageOrigin {
  const from = originIn(state);
  const throughGame = (state as Partial<GamePageState> | null)?.throughGame === true;
  return { screen: from ?? UNSAID_ORIGIN, returns: from !== null && !throughGame };
}
