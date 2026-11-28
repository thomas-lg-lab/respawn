import type { HttpTestingController } from '@angular/common/http/testing';
import { type QueryClient } from '@tanstack/angular-query';
import type { LibraryGame } from '../models/models';
import { TEST_LIBRARY_HISTORY, TEST_LIBRARY_STATS, TEST_LIBRARY_YEARS } from '../testing/fixtures';
import { type QueriesSetUp, setUpQueries } from '../testing/queries';
import { LibraryQueries, libraryKeys } from './library.queries';

function game(index: number): LibraryGame {
  return {
    game_id: `00000000-0000-4000-8000-${String(index).padStart(12, '0')}`,
    name: `Game ${index}`,
    playtime_seconds: index * 60,
    recent_playtime_seconds: null,
    last_played_at: null,
    entries: [],
    cover_source: null,
    genres: [],
    themes: [],
    normally_seconds: null,
  };
}

describe('LibraryQueries', () => {
  let queries: LibraryQueries;
  let client: QueryClient;
  let http: HttpTestingController;
  let request: QueriesSetUp<LibraryQueries>['request'];

  beforeEach(() => {
    ({ queries, client, http, request } = setUpQueries(LibraryQueries));
  });

  afterEach(() => http.verify());

  it('reads the whole library, page after page', async () => {
    const first = Array.from({ length: 500 }, (_, index) => game(index));
    const games = client.fetchQuery(queries.games());

    (await request('/api/v1/library?sort=name&offset=0&limit=500')).flush({
      total: 502,
      offset: 0,
      limit: 500,
      games: first,
    });
    (await request('/api/v1/library?sort=name&offset=500&limit=500')).flush({
      total: 502,
      offset: 500,
      limit: 500,
      games: [game(500), game(501)],
    });

    expect(await games).toHaveLength(502);
  });

  it('reads an empty library in one request', async () => {
    const games = client.fetchQuery(queries.games());
    (await request('/api/v1/library?sort=name&offset=0&limit=500')).flush({
      total: 0,
      offset: 0,
      limit: 500,
      games: [],
    });

    expect(await games).toEqual([]);
  });

  it('stops on an empty page, whatever the total says', async () => {
    const games = client.fetchQuery(queries.games());
    (await request('/api/v1/library?sort=name&offset=0&limit=500')).flush({
      total: 3,
      offset: 0,
      limit: 500,
      games: [game(0)],
    });
    (await request('/api/v1/library?sort=name&offset=1&limit=500')).flush({
      total: 3,
      offset: 1,
      limit: 500,
      games: [],
    });

    expect(await games).toHaveLength(1);
  });

  it('reads the figures of the library', async () => {
    const stats = client.fetchQuery(queries.stats());
    (await request('/api/v1/library/stats')).flush(TEST_LIBRARY_STATS);

    expect(await stats).toEqual(TEST_LIBRARY_STATS);
  });

  it('reads the whole history, from its first day', async () => {
    const history = client.fetchQuery(queries.history());
    (await request('/api/v1/library/history')).flush(TEST_LIBRARY_HISTORY);

    expect(await history).toEqual(TEST_LIBRARY_HISTORY);
  });

  it('holds the history with the rest of the library, so a sync that ends marks it stale', () => {
    expect(queries.history().queryKey.slice(0, 1)).toEqual([...libraryKeys.everything]);
  });

  it('reads the years', async () => {
    const years = client.fetchQuery(queries.years());
    (await request('/api/v1/library/years')).flush(TEST_LIBRARY_YEARS);

    expect(await years).toEqual(TEST_LIBRARY_YEARS);
  });

  it('holds the years with the rest of the library, so a sync that ends marks them stale', () => {
    expect(queries.years().queryKey.slice(0, 1)).toEqual([...libraryKeys.everything]);
  });
});
