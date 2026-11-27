import type { HttpTestingController } from '@angular/common/http/testing';
import { MutationObserver, type QueryClient } from '@tanstack/angular-query';
import { libraryKeys } from '../library/library.queries';
import type { Game, GameCovers } from '../models/models';
import { TEST_LIBRARY_STATS } from '../testing/fixtures';
import { type QueriesSetUp, setUpQueries } from '../testing/queries';
import { GameQueries, gameKeys } from './game.queries';

const ID = '00000000-0000-4000-8000-000000000001';

/** Only what these tests read of a game: its covers. */
const GAME = { covers: { sources: ['steam', 'igdb'], chosen: null } } as Game;

describe('GameQueries', () => {
  let queries: GameQueries;
  let client: QueryClient;
  let http: HttpTestingController;
  let request: QueriesSetUp<GameQueries>['request'];

  beforeEach(() => {
    ({ queries, client, http, request } = setUpQueries(GameQueries));
  });

  afterEach(() => http.verify());

  const stale = (key: readonly string[]) => client.getQueryState(key)?.isInvalidated;

  it('reads a game, and a refusal is not asked again', async () => {
    const game = client.fetchQuery(queries.game(ID)).catch((thrown: unknown) => thrown);
    (await request(`/api/v1/games/${ID}`)).flush(
      { error: { code: 'unknown_game', message: 'In English, for the log.' } },
      { status: 404, statusText: '' },
    );

    expect(await game).toMatchObject({ status: 404, code: 'unknown_game' });
  });

  it('chooses a cover: the game takes the answer at once, the library is asked again', async () => {
    const chosen: GameCovers = { sources: ['steam', 'igdb'], chosen: 'igdb' };
    client.setQueryData(gameKeys.one(ID), GAME);
    client.setQueryData(gameKeys.months(ID), []);
    client.setQueryData(libraryKeys.games, []);
    client.setQueryData(libraryKeys.stats, TEST_LIBRARY_STATS);
    const choose = new MutationObserver(client, queries.chooseCover());

    const done = choose.mutate({ id: ID, source: 'igdb' });
    const sent = await request(`/api/v1/games/${ID}/cover-choice`);
    expect(sent.request.method).toBe('PUT');
    expect(sent.request.body).toEqual({ source: 'igdb' });
    sent.flush(chosen);
    await done;

    expect(client.getQueryData<Game>(gameKeys.one(ID))?.covers).toEqual(chosen);
    expect(stale(libraryKeys.games)).toBe(true);
    // A cover changes no figure and no month.
    expect(stale(libraryKeys.stats)).toBe(false);
    expect(stale(gameKeys.months(ID))).toBe(false);
  });

  it("goes back to the game's own cover by taking the choice away", async () => {
    const choose = new MutationObserver(client, queries.chooseCover());

    const done = choose.mutate({ id: ID, source: null });
    const sent = await request(`/api/v1/games/${ID}/cover-choice`);
    expect(sent.request.method).toBe('DELETE');
    sent.flush(GAME.covers);
    await done;
  });

  it('takes a game out and puts it back: the whole library is stale each time', async () => {
    for (const [mutation, method] of [
      [queries.takeOut(), 'DELETE'],
      [queries.putBack(), 'PUT'],
    ] as const) {
      client.setQueryData(libraryKeys.stats, TEST_LIBRARY_STATS);
      client.setQueryData(gameKeys.one(ID), GAME);

      const done = new MutationObserver(client, mutation).mutate(ID);
      const sent = await request(`/api/v1/library/games/${ID}`);
      expect(sent.request.method).toBe(method);
      sent.flush(null, { status: 204, statusText: '' });
      await done;

      expect(stale(libraryKeys.stats)).toBe(true);
      expect(stale(gameKeys.one(ID))).toBe(true);
    }
  });

  it('dismisses a notice: the game is asked again, its months are not', async () => {
    client.setQueryData(gameKeys.one(ID), GAME);
    client.setQueryData(gameKeys.months(ID), []);
    const dismiss = new MutationObserver(client, queries.dismissNotice());

    const done = dismiss.mutate(ID);
    const sent = await request(`/api/v1/library/removed/${ID}/notice`);
    expect(sent.request.method).toBe('DELETE');
    sent.flush(null, { status: 204, statusText: '' });
    await done;

    expect(stale(gameKeys.one(ID))).toBe(true);
    expect(stale(gameKeys.months(ID))).toBe(false);
  });
});
