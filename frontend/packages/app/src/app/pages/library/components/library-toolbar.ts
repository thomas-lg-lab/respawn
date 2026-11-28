import { Component, computed, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideX } from '@lucide/angular';
import { Button, Pill, PillGroup, Select } from '@respawn/ui';
import { formatCount } from '@app/format/format';
import {
  FILTERS,
  LENGTHS,
  type LibraryFilter,
  type LibraryLength,
  type LibrarySort,
  type LibraryView,
  SORTS,
  VIEWS,
  shortTag,
} from '@app/games/game-list';
import { type FilterChoice, FilterMenu } from './filter-menu';

/**
 * A filter shown as a chip: a tag, shortened when long, with its whole name kept; or a length,
 * or a span of neighbouring lengths read as one, said by its key of the translations.
 */
interface Chip {
  readonly key: string;
  readonly label: string;
  /** A tag's name as the game database or the store writes it; none for a length. */
  readonly full: string | null;
  readonly tags: readonly string[];
  /** The store's tags it stands for, by key. */
  readonly storeTags: readonly string[];
  readonly lengths: readonly LibraryLength[];
}

/** Neighbouring lengths picked together, read as one span. */
const SPANS: Record<string, string> = {
  'short,medium': 'filters.span.shortMedium',
  'medium,long': 'filters.span.mediumLong',
  'short,medium,long': 'filters.span.known',
};

/**
 * What narrows, orders and shows the library: the filters as a row above the rest, each with
 * how many games it would keep, the title that was searched with the way back to every game,
 * the games as covers or as a list, and their order. It holds none of it: the screen does.
 */
@Component({
  selector: 'rs-library-toolbar',
  imports: [TranslocoDirective, Button, Pill, PillGroup, Select, FilterMenu, LucideX],
  templateUrl: './library-toolbar.html',
})
export class LibraryToolbar {
  /** The title the header's search asked for; empty when the whole library is listed. */
  readonly search = input.required<string>();
  /** The genres and themes the library is narrowed to: the games that bear them all. */
  readonly tags = input<readonly string[]>([]);
  /** The lengths the library is narrowed to: the games in any of them. */
  readonly lengths = input<readonly LibraryLength[]>([]);
  /** The stores' tags the library is narrowed to, each with its name: the games that bear them all. */
  readonly storeTags = input<readonly { key: string; label: string }[]>([]);
  readonly genreChoices = input<readonly FilterChoice[]>([]);
  readonly themeChoices = input<readonly FilterChoice[]>([]);
  readonly storeTagChoices = input<readonly FilterChoice[]>([]);
  readonly lengthChoices = input<readonly FilterChoice[]>([]);
  /** How many games the list holds, as a phone's menu says it. */
  readonly listed = input<{ one: boolean; none: boolean; count: string }>({
    one: false,
    none: true,
    count: '0',
  });
  readonly filter = input.required<LibraryFilter>();
  readonly sort = input.required<LibrarySort>();
  readonly view = input.required<LibraryView>();
  readonly counts = input.required<Record<LibraryFilter, number>>();
  readonly language = input.required<string>();

  readonly searchCleared = output<void>();
  readonly tagToggled = output<string>();
  readonly tagsCleared = output<'genres' | 'themes'>();
  readonly tagsDropped = output<readonly string[]>();
  readonly storeTagToggled = output<string>();
  readonly storeTagsDropped = output<readonly string[]>();
  readonly storeTagsCleared = output<void>();
  readonly lengthToggled = output<string>();
  readonly lengthsDropped = output<readonly LibraryLength[]>();
  readonly filterPicked = output<LibraryFilter>();
  readonly sortPicked = output<LibrarySort>();
  readonly viewPicked = output<LibraryView>();

  protected readonly allLengths = LENGTHS;
  protected readonly sorts = SORTS;
  protected readonly views = VIEWS;

  protected readonly filters = computed(() =>
    FILTERS.map((id) => ({ id, count: formatCount(this.counts()[id], this.language()) })),
  );

  /**
   * What the menus keep, as chips: the genres and themes, the stores' tags, then the lengths,
   * neighbours read as one span.
   */
  protected readonly chips = computed<Chip[]>(() => {
    const tags: Chip[] = this.tags().map((tag) => ({
      key: 'tag:' + tag,
      label: shortTag(tag),
      full: tag,
      tags: [tag],
      storeTags: [],
      lengths: [],
    }));
    const storeTags: Chip[] = this.storeTags().map((tag) => ({
      key: 'store-tag:' + tag.key,
      label: tag.label,
      full: tag.label,
      tags: [],
      storeTags: [tag.key],
      lengths: [],
    }));
    const known = this.lengths().filter((length) => length !== 'unknown');
    const span = SPANS[known.join(',')];
    const lengths: Chip[] = span
      ? [
          {
            key: 'length:' + known.join(','),
            label: span,
            full: null,
            tags: [],
            storeTags: [],
            lengths: known,
          },
        ]
      : known.map((length) => ({
          key: 'length:' + length,
          label: 'filters.length.' + length,
          full: null,
          tags: [],
          storeTags: [],
          lengths: [length],
        }));
    if (this.lengths().includes('unknown')) {
      lengths.push({
        key: 'length:unknown',
        label: 'filters.length.unknown',
        full: null,
        tags: [],
        storeTags: [],
        lengths: ['unknown'],
      });
    }
    return [...tags, ...storeTags, ...lengths];
  });

  protected drop(chip: Chip): void {
    if (chip.tags.length) this.tagsDropped.emit(chip.tags);
    if (chip.storeTags.length) this.storeTagsDropped.emit(chip.storeTags);
    if (chip.lengths.length) this.lengthsDropped.emit(chip.lengths);
  }

  protected pickSort(event: Event): void {
    const picked = (event.target as HTMLSelectElement).value;
    const sort = SORTS.find((each) => each === picked);
    if (sort) this.sortPicked.emit(sort);
  }
}
