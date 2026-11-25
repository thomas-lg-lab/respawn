import { Component, computed, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Button, Pill, Select } from '@respawn/ui';
import { formatCount } from '@app/format/format';
import { FILTERS, type LibraryFilter, type LibrarySort, SORTS } from '@app/games/game-list';

/**
 * What narrows and orders the library: the filters as a row above the rest, each with how many
 * games it would keep, the title that was searched with the way back to every game, and the
 * order of the list. It holds none of it: the screen does.
 */
@Component({
  selector: 'rs-library-toolbar',
  imports: [TranslocoDirective, Button, Pill, Select],
  templateUrl: './library-toolbar.html',
})
export class LibraryToolbar {
  /** The title the header's search asked for; empty when the whole library is listed. */
  readonly search = input.required<string>();
  readonly filter = input.required<LibraryFilter>();
  readonly sort = input.required<LibrarySort>();
  readonly counts = input.required<Record<LibraryFilter, number>>();
  readonly language = input.required<string>();

  readonly searchCleared = output<void>();
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
