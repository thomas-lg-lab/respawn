import { Service, inject } from '@angular/core';
import { QueryClient, mutationOptions, queryOptions } from '@tanstack/angular-query';
import { Api } from '../api/api';
import { libraryKeys } from '../library/library.queries';
import type { Game, GameMonths, LibraryMonth } from '../models/models';

/**
 * The keys of what the cache holds about one game and one month. They are under the
 * library's: what a sync read changes them too, and the end of a sync marks all of it stale.
 */
export const gameKeys = {
  one: (id: string) => [...libraryKeys.everything, 'game', id] as const,
  months: (id: string) => [...libraryKeys.everything, 'game', id, 'months'] as const,
  month: (month: string) => [...libraryKeys.everything, 'month', month] as const,
};

/**
 * The server state of one game as its owner sees it: the game, what was played of it month by
 * month, what else was played in a month, and taking it out of the library or putting it back.
 */
@Service()
export class GameQueries {
  private readonly api = inject(Api);
  private readonly client = inject(QueryClient);

  /** The game: its record, what the account owns of it, its rank, and whether it was taken out. */
  game(id: string) {
    return queryOptions({
      queryKey: gameKeys.one(id),
      queryFn: () => this.api.get<Game>(`/games/${encodeURIComponent(id)}`),
      staleTime: Infinity,
      retry: false,
    });
  }

  /** The months the game was played in, the earliest first, and the time of each. */
  months(id: string) {
    return queryOptions({
      queryKey: gameKeys.months(id),
      queryFn: () => this.api.get<GameMonths>(`/games/${encodeURIComponent(id)}/months`),
      staleTime: Infinity,
      retry: false,
    });
  }

  /** The games played in a month (`YYYY-MM`), the most played first. */
  month(month: string) {
    return queryOptions({
      queryKey: gameKeys.month(month),
      queryFn: () => this.api.get<LibraryMonth>(`/library/months/${encodeURIComponent(month)}`),
      staleTime: Infinity,
      retry: false,
    });
  }

  /**
   * Takes a game its store no longer lists out of the library. The library and its figures
   * no longer count it: all of it is stale.
   */
  takeOut() {
    return mutationOptions({
      mutationFn: (id: string) => this.api.delete<void>(`/library/games/${encodeURIComponent(id)}`),
      onSuccess: () => this.libraryChanged(),
    });
  }

  /** Puts a game taken out back in the library, whether its store lists it again or not. */
  putBack() {
    return mutationOptions({
      mutationFn: (id: string) =>
        this.api.put<void>(`/library/games/${encodeURIComponent(id)}`, null),
      onSuccess: () => this.libraryChanged(),
    });
  }

  /** The store lists a game taken out again, and its owner leaves it out: said no more. */
  dismissNotice() {
    return mutationOptions({
      mutationFn: (id: string) =>
        this.api.delete<void>(`/library/removed/${encodeURIComponent(id)}/notice`),
      onSuccess: (_, id) => this.client.invalidateQueries({ queryKey: gameKeys.one(id) }),
    });
  }

  private libraryChanged(): Promise<void> {
    return this.client.invalidateQueries({ queryKey: libraryKeys.everything });
  }
}
