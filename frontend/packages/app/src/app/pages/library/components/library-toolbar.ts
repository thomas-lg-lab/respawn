import { Component, computed, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Button, Pill, PillGroup, Select } from '@respawn/ui';
import { formatCount } from '@app/format/format';
import {
  FILTERS,
  type LibraryFilter,
  type LibrarySort,
  type LibraryView,
  SORTS,
  VIEWS,
} from '@app/games/game-list';

/**
 * What narrows, orders and shows the library: the filters as a row above the rest, each with
 * how many games it would keep, the title that was searched with the way back to every game,
 * the games as covers or as a list, and their order. It holds none of it: the screen does.
 */
@Component({
  selector: 'rs-library-toolbar',
  imports: [TranslocoDirective, Button, Pill, PillGroup, Select],
  templateUrl: './library-toolbar.html',
})
export class LibraryToolbar {
  /** The title the header's search asked for; empty when the whole library is listed. */
  readonly search = input.required<string>();
  /** The genre or theme the library is narrowed to; none when it is not. */
  readonly genre = input('');
  readonly filter = input.required<LibraryFilter>();
  readonly sort = input.required<LibrarySort>();
  readonly view = input.required<LibraryView>();
  readonly counts = input.required<Record<LibraryFilter, number>>();
  readonly language = input.required<string>();

  readonly searchCleared = output<void>();
  readonly genreCleared = output<void>();
  readonly filterPicked = output<LibraryFilter>();
  readonly sortPicked = output<LibrarySort>();
  readonly viewPicked = output<LibraryView>();

  protected readonly sorts = SORTS;
  protected readonly views = VIEWS;

  protected readonly filters = computed(() =>
    FILTERS.map((id) => ({ id, count: formatCount(this.counts()[id], this.language()) })),
  );

  protected pickSort(event: Event): void {
    const picked = (event.target as HTMLSelectElement).value;
    const sort = SORTS.find((each) => each === picked);
    if (sort) this.sortPicked.emit(sort);
  }
}
