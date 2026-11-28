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
import type { FilterChoice } from './components/filter-menu';
import { LibraryToolbar } from './components/library-toolbar';
import {
  LIBRARY_FILTER,
  LIBRARY_GENRE,
  LIBRARY_LENGTH,
  LIBRARY_SEARCH,
  LIBRARY_SORT,
  LIBRARY_TAG,
  type LibraryFilter,
  type LibraryLength,
  type LibrarySort,
  type LibraryView,
  VIEWS,
  LENGTHS,
  askedFilter,
  askedLengths,
  askedSort,
  askedTags,
  filterCounts,
  kept,
  lengthCounts,
  letters,
  longestPlaytime,
  matching,
  sorted,
  storeTagCounts,
  storeTagName,
  tagCounts,
  withLengths,
  withStoreTags,
  withTags,
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
  /**
   * The names of the stores' tags, asked once a game bears one: while they are not read, a tag
   * is shown by its key.
   */
  private readonly storeTagNames = injectQuery(() => ({
    ...this.queries.storeTags(),
    enabled: (this.games.data() ?? []).some((game) => game.store_tags.length > 0),
  }));
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
  /**
   * The genres and themes the library is narrowed to, as the game database names them: the games
   * that bear them all. A game's page leads there with one of its own.
   */
  protected readonly tags = this.asked((words) => askedTags(words.getAll(LIBRARY_GENRE)));
  /** The stores' tags the library is narrowed to, by key: the games that bear them all. */
  protected readonly storeTags = this.asked((words) => askedTags(words.getAll(LIBRARY_TAG)));
  /** The lengths the library is narrowed to: the games in any of them. */
  protected readonly lengths = this.asked((words) => askedLengths(words.getAll(LIBRARY_LENGTH)));
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
    source: () =>
      [
        this.search(),
        this.tags().join(','),
        this.storeTags().join(','),
        this.lengths().join(','),
        this.filter(),
        this.sort(),
        this.arrivals(),
      ].join('\n'),
    computation: () => keptSteps(this.location.getState()),
  });

  /** How many games the library holds, in the reader's language, and whether that is one. */
  protected readonly owned = computed(() => this.counted(this.games.data()?.length ?? 0));

  /** What the search keeps: everything else narrows it. */
  private readonly found = computed(() => matching(this.games.data() ?? [], this.search()));
  /** What the tags and the lengths keep of it: the filters count among these. */
  protected readonly counts = computed(() =>
    filterCounts(
      withLengths(
        withStoreTags(withTags(this.found(), this.tags()), this.storeTags()),
        this.lengths(),
      ),
    ),
  );
  /** What the filter keeps; then the tags; then the lengths: the list. */
  private readonly filtered = computed(() => kept(this.found(), this.filter()));
  private readonly tagged = computed(() =>
    withStoreTags(withTags(this.filtered(), this.tags()), this.storeTags()),
  );
  private readonly narrowed = computed(() => withLengths(this.tagged(), this.lengths()));

  private readonly listed = computed(() =>
    sorted(this.narrowed(), this.sort(), this.language.current()),
  );

  /** Each genre or theme the games listed bear, with how many; the picked ones first. */
  protected readonly genreChoices = computed(() => this.tagChoices('genres'));
  protected readonly themeChoices = computed(() => this.tagChoices('themes'));
  /** Each name of the stores' tags, by key, in each language the store gave. */
  private readonly namesByKey = computed(
    () =>
      new Map((this.storeTagNames.data()?.tags ?? []).map((tag) => [tag.key, tag.names] as const)),
  );
  /** Each tag of the stores the games listed bear, named in the reader's language. */
  protected readonly storeTagChoices = computed<FilterChoice[]>(() => {
    const language = this.language.current();
    const names = this.namesByKey();
    return storeTagCounts(this.narrowed(), this.storeTags()).map((each) => ({
      id: each.tag,
      label: storeTagName(each.tag, names, language),
      count: formatCount(each.count, language),
      picked: this.storeTags().includes(each.tag),
    }));
  });
  /** The picked tags of the stores, as their chips name them. */
  protected readonly storeTagLabels = computed(() => {
    const language = this.language.current();
    const names = this.namesByKey();
    return this.storeTags().map((key) => ({ key, label: storeTagName(key, names, language) }));
  });
  /**
   * Each length with how many games the other filters keep in it: a game has one, so the
   * lengths picked do not narrow one another's count.
   */
  protected readonly lengthChoices = computed<FilterChoice[]>(() => {
    const language = this.language.current();
    const counts = lengthCounts(this.tagged());
    return LENGTHS.map((id) => ({
      id,
      label: id,
      count: formatCount(counts[id], language),
      picked: this.lengths().includes(id),
    }));
  });
  /** How many games the list holds, as a phone's menu says it on its last button. */
  protected readonly listedCount = computed(() => ({
    ...this.counted(this.listed().length),
    none: this.listed().length === 0,
  }));
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

  /** A genre or a theme picked, or unpicked: the address says it, the history gains no step. */
  protected toggleTag(tag: string): void {
    const tags = this.tags();
    this.rewrite(
      {
        [LIBRARY_GENRE]: tags.includes(tag) ? tags.filter((each) => each !== tag) : [...tags, tag],
      },
      true,
    );
  }

  /** Some of the genres and themes taken off. */
  protected dropTags(dropped: readonly string[]): void {
    this.rewrite({ [LIBRARY_GENRE]: this.tags().filter((tag) => !dropped.includes(tag)) }, true);
  }

  /** The genres, or the themes, all taken off. */
  protected clearTags(kind: 'genres' | 'themes'): void {
    const ofKind = new Set((this.games.data() ?? []).flatMap((game) => game[kind]));
    this.dropTags(this.tags().filter((tag) => ofKind.has(tag)));
  }

  /** A tag of the stores picked, or unpicked. */
  protected toggleStoreTag(key: string): void {
    const keys = this.storeTags();
    this.rewrite(
      {
        [LIBRARY_TAG]: keys.includes(key) ? keys.filter((each) => each !== key) : [...keys, key],
      },
      true,
    );
  }

  /** Some of the stores' tags taken off; all of them for none given. */
  protected dropStoreTags(dropped?: readonly string[]): void {
    this.rewrite(
      { [LIBRARY_TAG]: dropped ? this.storeTags().filter((key) => !dropped.includes(key)) : [] },
      true,
    );
  }

  /** A length picked, or unpicked; the address keeps them in their own order. */
  protected toggleLength(length: string): void {
    const picked = LENGTHS.find((each) => each === length);
    if (!picked) return;
    const lengths = this.lengths();
    this.rewrite(
      {
        [LIBRARY_LENGTH]: lengths.includes(picked)
          ? lengths.filter((each) => each !== picked)
          : LENGTHS.filter((each) => each === picked || lengths.includes(each)),
      },
      true,
    );
  }

  /** Some of the lengths taken off. */
  protected dropLengths(dropped: readonly LibraryLength[]): void {
    const kept = this.lengths().filter((length) => !dropped.includes(length));
    this.rewrite({ [LIBRARY_LENGTH]: kept }, true);
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
  private rewrite(words: Record<string, string | string[] | null>, inPlace = false): void {
    const there = this.router.parseUrl(withoutSaidOnce(this.router, this.router.url));
    there.queryParams = Object.fromEntries(
      Object.entries({ ...there.queryParams, ...words }).filter(
        ([, value]) => value !== null && !(Array.isArray(value) && value.length === 0),
      ),
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

  /** The genres or the themes of the games listed, as a menu offers them. */
  private tagChoices(kind: 'genres' | 'themes'): FilterChoice[] {
    const language = this.language.current();
    return tagCounts(this.narrowed(), this.games.data() ?? [], kind, this.tags()).map((each) => ({
      id: each.tag,
      label: each.tag,
      count: formatCount(each.count, language),
      picked: this.tags().includes(each.tag),
    }));
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
