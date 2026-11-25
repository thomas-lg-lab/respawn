import { Component, computed, input, model, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Pill, SearchField, Select } from '@respawn/ui';
import { formatCount } from '@app/format/format';
import { FILTERS, type LibraryFilter, type LibrarySort, SORTS } from '@app/games/game-list';

/**
 * What narrows and orders the library: the filters as a row above the rest, each with how many
 * games it would keep, a search by title, and the order of the list. It holds none of it: the
 * screen does.
 */
@Component({
  selector: 'rs-library-toolbar',
  imports: [TranslocoDirective, Pill, SearchField, Select],
  templateUrl: './library-toolbar.html',
})
export class LibraryToolbar {
  readonly search = model.required<string>();
  readonly filter = input.required<LibraryFilter>();
  readonly sort = input.required<LibrarySort>();
  readonly counts = input.required<Record<LibraryFilter, number>>();
  readonly language = input.required<string>();

  readonly filterPicked = output<LibraryFilter>();
  readonly sortPicked = output<LibrarySort>();

  protected readonly sorts = SORTS;

  protected readonly filters = computed(() =>
    FILTERS.map((id) => ({ id, count: formatCount(this.counts()[id], this.language()) })),
  );

  protected pickSort(event: Event): void {
    const picked = (event.target as HTMLSelectElement).value;
    const sort = SORTS.find((each) => each === picked);
    if (sort) this.sortPicked.emit(sort);
  }
}
