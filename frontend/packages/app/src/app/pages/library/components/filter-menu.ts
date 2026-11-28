import { Component, computed, input, output, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideChevronDown, LucideSearch } from '@lucide/angular';
import { Button, Disclosure, DisclosurePanel, DisclosureTrigger } from '@respawn/ui';

/** What a filter menu offers: one row per choice, with how many games it would keep. */
export interface FilterChoice {
  readonly id: string;
  /** As the row shows it: a tag as the game database names it; a length is said by its id. */
  readonly label: string;
  readonly count: string;
  readonly picked: boolean;
}

/** Lower case and without accents, so "role" finds "Role-playing". */
function plain(text: string): string {
  return text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase();
}

/**
 * One dimension of the library's filters (the genres, the themes, the lengths): a button that
 * says how many of its choices are picked, and a panel of them to tick, with a field to find one
 * when there are many, the rule several picked follow, and the way to clear them. Under a
 * computer's pointer the panel opens under its button; on a phone it takes the toolbar's width,
 * and closes on "See N games". It holds none of the choices: the screen does.
 */
@Component({
  selector: 'rs-filter-menu',
  imports: [
    TranslocoDirective,
    Button,
    Disclosure,
    DisclosurePanel,
    DisclosureTrigger,
    LucideChevronDown,
    LucideSearch,
  ],
  templateUrl: './filter-menu.html',
})
export class FilterMenu {
  readonly kind = input.required<'genres' | 'themes' | 'lengths'>();
  readonly choices = input.required<readonly FilterChoice[]>();
  /** How many games the list would show, as "See N games" says it. */
  readonly shown = input.required<{ one: boolean; none: boolean; count: string }>();

  readonly toggled = output<string>();
  readonly cleared = output<void>();

  /** A field to find a choice, for the tags alone: the lengths are four. */
  protected readonly searchable = computed(() => this.kind() !== 'lengths');
  protected readonly typed = signal('');

  protected readonly picked = computed(
    () => this.choices().filter((choice) => choice.picked).length,
  );

  protected readonly listed = computed(() => {
    const needle = plain(this.typed().trim());
    return needle
      ? this.choices().filter((choice) => plain(choice.label).includes(needle))
      : this.choices();
  });

  /** Opened again, the menu shows every choice. */
  protected openChanged(open: boolean): void {
    if (open) this.typed.set('');
  }

  protected find(event: Event): void {
    this.typed.set((event.target as HTMLInputElement).value);
  }
}
