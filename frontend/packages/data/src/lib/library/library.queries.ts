import { Service, inject } from '@angular/core';
import { queryOptions } from '@tanstack/angular-query';
import { Api } from '../api/api';
import type {
  Library,
  LibraryGame,
  LibraryHistory,
  LibraryStats,
  LibraryYears,
  StoreTags,
} from '../models/models';

/** The keys of what the cache holds about the library: all of it under `['library']`. */
export const libraryKeys = {
  everything: ['library'] as const,
  games: ['library', 'games'] as const,
  stats: ['library', 'stats'] as const,
  history: ['library', 'history'] as const,
  years: ['library', 'years'] as const,
  storeTags: ['library', 'store-tags'] as const,
};

/** The most games the API gives in one answer. */
const PAGE = 500;

/**
 * The server state of the library: its games, its figures, its history and its years. All change only when a sync
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
    });
  }

  /** The figures of the library: how many games, how much time, since when it is followed. */
  stats() {
    return queryOptions({
      queryKey: libraryKeys.stats,
      queryFn: () => this.api.get<LibraryStats>('/library/stats'),
      staleTime: Infinity,
    });
  }

  /**
   * What was played day by day since the first sync, all of it: the years are counted over
   * the whole, and the latest days are its end.
   */
  history() {
    return queryOptions({
      queryKey: libraryKeys.history,
      queryFn: () => this.api.get<LibraryHistory>('/library/history'),
      staleTime: Infinity,
    });
  }

  /**
   * The names of the tags the stores' players gave the games, in each language the store gives:
   * the games carry their tags by key. Read with the games, and marked stale with them.
   */
  storeTags() {
    return queryOptions({
      queryKey: libraryKeys.storeTags,
      queryFn: () => this.api.get<StoreTags>('/store-tags'),
      staleTime: Infinity,
    });
  }

  /**
   * What was played year by year: from Steam's Year in Review for the years it covers, and
   * from the syncs for the others. A review imported meanwhile shows at the next sync.
   */
  years() {
    return queryOptions({
      queryKey: libraryKeys.years,
      queryFn: () => this.api.get<LibraryYears>('/library/years'),
      staleTime: Infinity,
    });
  }
}
