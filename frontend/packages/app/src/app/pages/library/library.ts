import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  ActivatedRoute,
  NavigationEnd,
  NavigationStart,
  Router,
  RouterLink,
} from '@angular/router';
import { map } from 'rxjs';
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
  LIBRARY_SEARCH,
  type LibraryFilter,
  type LibrarySort,
  type LibraryView,
  VIEWS,
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
  protected readonly search = toSignal(
    this.route.queryParamMap.pipe(map((asked) => searched(asked.get(LIBRARY_SEARCH)))),
    { initialValue: searched(this.route.snapshot.queryParamMap.get(LIBRARY_SEARCH)) },
  );
  protected readonly filter = signal<LibraryFilter>('all');
  protected readonly sort = signal<LibrarySort>('last_played');
  protected readonly view = signal<LibraryView>(readChoice(VIEW_KEY, VIEWS) ?? 'covers');
  /** How many steps of the list are shown: back to one whenever the list changes. */
  private readonly steps = linkedSignal({ source: this.search, computation: () => 1 });

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

  constructor() {
    // Back to every game whenever a title is searched, the one already shown too: the search
    // comes from the header, and a filter left on would hide the very game that was asked
    // for. Clearing the search asks for nothing new: the filter stays. So does it when the
    // browser's history leads back to a title: nothing was searched then.
    let throughHistory = false;
    this.router.events.pipe(takeUntilDestroyed()).subscribe((event) => {
      if (event instanceof NavigationStart) {
        throughHistory = event.navigationTrigger === 'popstate';
      }
      if (!(event instanceof NavigationEnd) || throughHistory || !this.search()) return;
      this.filter.set('all');
      this.steps.set(1);
    });
  }

  /**
   * The whole library again: the title leaves the address, whatever else it says stays. Not
   * the words it carried for one arrival, which the router still holds: they were said.
   */
  protected clearSearch(): void {
    const there = this.router.parseUrl(withoutSaidOnce(this.router, this.router.url));
    there.queryParams = Object.fromEntries(
      Object.entries(there.queryParams).filter(([word]) => word !== LIBRARY_SEARCH),
    );
    void this.router.navigateByUrl(there);
  }

  protected pickFilter(filter: LibraryFilter): void {
    this.filter.set(filter);
    this.steps.set(1);
  }

  protected pickSort(sort: LibrarySort): void {
    this.sort.set(sort);
    this.steps.set(1);
  }

  /** Another way to show the same games: the games already shown stay. */
  protected pickView(view: LibraryView): void {
    this.view.set(view);
    writeChoice(VIEW_KEY, view);
  }

  protected showMore(): void {
    this.steps.update((steps) => steps + 1);
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
