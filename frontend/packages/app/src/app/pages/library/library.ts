import { Location } from '@angular/common';
import { Component, type Signal, computed, inject, linkedSignal, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, type ParamMap, Router, RouterLink } from '@angular/router';
import { filter, map, scan } from 'rxjs';
import { TranslocoDirective } from '@jsverse/transloco';
import { LibraryQueries } from '@respawn/data';
import { Banner, Button, Skeleton } from '@respawn/ui';
import { readChoice, writeChoice } from '@respawn/util';
import { injectQuery } from '@tanstack/angular-query';
import { failureKey } from '@app/errors/failure-message';
import { withoutSaidOnce } from '@app/session/said-once';
import { formatCount, isOne } from '@app/format/format';
import { LanguageSetting } from '@app/i18n/language';
import { GameGrid } from './components/game-grid';
import { GameTable } from './components/game-table';
import { LibraryToolbar } from './components/library-toolbar';
import {
  LIBRARY_FILTER,
  LIBRARY_SEARCH,
  LIBRARY_SORT,
  type LibraryFilter,
  type LibrarySort,
  type LibraryView,
  VIEWS,
  askedFilter,
  askedSort,
  filterCounts,
  kept,
  letters,
  longestPlaytime,
  matching,
  sorted,
} from '@app/games/game-list';

/** How many games a "show more" adds: a list of a thousand rows is not drawn at once. */
const STEP = 100;

/** Where this browser keeps how the library is shown. */
const VIEW_KEY = 'respawn.library-view';

/** Under this name, the entry of the browser's history keeps how many steps its list showed. */
const STEPS_KEPT = 'librarySteps';

/** The steps an entry of the history kept, one for an entry that kept none. */
function keptSteps(state: unknown): number {
  const steps = (state as Record<string, unknown> | null)?.[STEPS_KEPT];
  return typeof steps === 'number' && Number.isInteger(steps) && steps > 1 ? steps : 1;
}

/** The title an address asks for, or none when it is made of nothing the search reads. */
function searched(asked: string | null): string {
  return asked !== null && letters(asked) > 0 ? asked.trim() : '';
}

/**
 * The library, as covers or as a list: every game of the linked accounts, filtered and sorted
 * here, in the browser, which holds the whole of it, and narrowed to the title the header's
 * search asked for. How it is shown is kept in this browser, a phone and a computer each
 * their own. The home says how the library gets there and where its sync stands; this screen
 * only shows what was read.
 */
@Component({
  selector: 'rs-library',
  imports: [
    RouterLink,
    TranslocoDirective,
    Banner,
    Button,
    Skeleton,
    GameGrid,
    GameTable,
    LibraryToolbar,
  ],
  templateUrl: './library.html',
})
export class Library {
  private readonly queries = inject(LibraryQueries);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly language = inject(LanguageSetting);

  protected readonly failureKey = failureKey;
  protected readonly currentLanguage = this.language.current;
  protected readonly placeholders = [0, 1, 2, 3, 4, 5, 6, 7];
  protected readonly games = injectQuery(() => this.queries.games());
  /** "Ago" is counted from the moment the games were read, so it follows each new reading. */
  protected readonly now = computed(() => this.games.dataUpdatedAt() || Date.now());

  /**
   * What is searched: the title on the address, which the header's search wrote there. It
   * stays on it, so a reload or a copy of the address shows the same games. A title made of
   * nothing the search reads (spaces, signs) is no search.
   */
  protected readonly search = this.asked((words) => searched(words.get(LIBRARY_SEARCH)));
  /**
   * The filter and the order, on the address too. A title searched from the header writes
   * neither: every game that bears it is shown, since a filter left on would hide the very
   * game that was asked for. Clearing the search asks for nothing new: they stay. When the
   * browser's history leads back, each address shows what it said.
   */
  protected readonly filter = this.asked((words) => askedFilter(words.get(LIBRARY_FILTER)));
  protected readonly sort = this.asked((words) => askedSort(words.get(LIBRARY_SORT)));
  protected readonly view = signal<LibraryView>(readChoice(VIEW_KEY, VIEWS) ?? 'covers');
  /**
   * Each navigation that ends here: a title searched again from the header leaves the address
   * as it was, and still shows the first games that bear it.
   */
  private readonly arrivals = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      scan((count) => count + 1, 0),
    ),
    { initialValue: 0 },
  );
  /**
   * How many steps of the list are shown: what the entry of the history says, read again at
   * each arrival. A list asked for anew is an entry that says nothing, so one step; the way
   * back from a game is the entry that was left, with the steps it had.
   */
  private readonly steps = linkedSignal({
    source: () => [this.search(), this.filter(), this.sort(), this.arrivals()].join('\n'),
    computation: () => keptSteps(this.location.getState()),
  });

  /** How many games the library holds, in the reader's language, and whether that is one. */
  protected readonly owned = computed(() => this.counted(this.games.data()?.length ?? 0));

  /** What the search keeps: the filters count among these. */
  private readonly found = computed(() => matching(this.games.data() ?? [], this.search()));
  protected readonly counts = computed(() => filterCounts(this.found()));

  private readonly listed = computed(() =>
    sorted(kept(this.found(), this.filter()), this.sort(), this.language.current()),
  );
  protected readonly shown = computed(() => this.listed().slice(0, this.steps() * STEP));
  protected readonly more = computed(() => this.listed().length > this.shown().length);

  /** "100 games shown out of 457": the words follow the first count. */
  protected readonly progress = computed(() => ({
    ...this.counted(this.shown().length),
    total: formatCount(this.listed().length, this.language.current()),
  }));

  /** A full bar is the most played game of the library, whatever is searched or filtered. */
  protected readonly scale = computed(() => longestPlaytime(this.games.data() ?? []));

  /** What the address says, read again at each of its changes. */
  private asked<T>(read: (words: ParamMap) => T): Signal<T> {
    return toSignal(this.route.queryParamMap.pipe(map(read)), {
      initialValue: read(this.route.snapshot.queryParamMap),
    });
  }

  /** The whole library again: the title leaves the address, whatever else it says stays. */
  protected clearSearch(): void {
    this.rewrite({ [LIBRARY_SEARCH]: null });
  }

  /**
   * Another filter or order: the address says it in place, the history gains no step, and
   * the window stays where it is.
   */
  protected pickFilter(filter: LibraryFilter): void {
    this.rewrite({ [LIBRARY_FILTER]: filter === 'all' ? null : filter }, true);
  }

  protected pickSort(sort: LibrarySort): void {
    this.rewrite({ [LIBRARY_SORT]: sort === 'last_played' ? null : sort }, true);
  }

  /**
   * This address with some of its words changed, `null` taking one off. Not with the words it
   * carried for one arrival, which the router still holds: they were said.
   */
  private rewrite(words: Record<string, string | null>, inPlace = false): void {
    const there = this.router.parseUrl(withoutSaidOnce(this.router, this.router.url));
    there.queryParams = Object.fromEntries(
      Object.entries({ ...there.queryParams, ...words }).filter(([, value]) => value !== null),
    );
    void this.router.navigateByUrl(
      there,
      inPlace ? { replaceUrl: true, scroll: 'manual' } : undefined,
    );
  }

  /** Another way to show the same games: the games already shown stay. */
  protected pickView(view: LibraryView): void {
    this.view.set(view);
    writeChoice(VIEW_KEY, view);
  }

  /**
   * One step more, written on the entry of the history beside what the router keeps there:
   * no navigation happens, and the way back to this entry finds the list as long.
   */
  protected showMore(): void {
    const steps = this.steps() + 1;
    this.steps.set(steps);
    this.location.replaceState(this.location.path(true), '', {
      ...(this.location.getState() as object | null),
      [STEPS_KEPT]: steps,
    });
  }

  /** One game or several is the language's to say: French counts none as one. */
  private counted(count: number): { one: boolean; count: string } {
    const language = this.language.current();
    return {
      one: isOne(count, language),
      count: formatCount(count, language),
    };
  }
}
