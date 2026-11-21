import { Service, inject } from '@angular/core';
import { queryOptions } from '@tanstack/angular-query';
import { Api } from '../api/api';
import type { Library, LibraryGame, LibraryStats } from '../models/models';

/** The keys of what the cache holds about the library: all of it under `['library']`. */
export const libraryKeys = {
  everything: ['library'] as const,
  games: ['library', 'games'] as const,
  stats: ['library', 'stats'] as const,
};

/** The most games the API gives in one answer. */
const PAGE = 500;

/**
 * The server state of the library: its games and its figures. Both change only when a sync
 * ends, and whatever learns that one did marks them stale (`libraryKeys.everything`); they
 * are not asked again on their own in between.
 */
@Service()
export class LibraryQueries {
  private readonly api = inject(Api);

  /**
   * Every game of the library, read page after page. The whole of it is kept here: a screen
   * searches, sorts and filters what it holds, and no two of them ask for the same games twice.
   */
  games() {
    return queryOptions({
      queryKey: libraryKeys.games,
      queryFn: async (): Promise<LibraryGame[]> => {
        const games: LibraryGame[] = [];
        for (;;) {
          const page = await this.api.get<Library>(
            `/library?sort=name&offset=${games.length}&limit=${PAGE}`,
          );
          games.push(...page.games);
          // An empty page ends it too: a total that moved meanwhile must not ask for ever.
          if (games.length >= page.total || page.games.length === 0) return games;
        }
      },
      staleTime: Infinity,
      retry: false,
    });
  }

  /** The figures of the library: how many games, how much time, since when it is followed. */
  stats() {
    return queryOptions({
      queryKey: libraryKeys.stats,
      queryFn: () => this.api.get<LibraryStats>('/library/stats'),
      staleTime: Infinity,
      retry: false,
    });
  }
}
