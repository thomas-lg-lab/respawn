import { Component, type ElementRef, input, model, viewChild } from '@angular/core';
import { LucideSearch } from '@lucide/angular';

/**
 * A search field: one line with its icon. It shows no label: its name is said to a screen
 * reader (`label`), and the placeholder hints at what can be typed. Both are the screen's
 * words. What the screen puts inside it stands after what is typed: a hint, a way to clear.
 *
 * With a list of suggestions under it (`popup`, the list's id), it is a combobox: it says
 * whether the list is shown and which suggestion the arrow keys are on. The list, and what a
 * key does, are the screen's.
 */
@Component({
  selector: 'rs-search-field',
  host: { '(mousedown)': 'pressed($event)' },
  imports: [LucideSearch],
  templateUrl: './search-field.html',
  styleUrl: './search-field.css',
})
export class SearchField {
  readonly label = input.required<string>();
  readonly placeholder = input('');
  readonly value = model('');

  /** The id of the list of suggestions, when the field has one. */
  readonly popup = input<string>();
  /** Whether that list is shown. */
  readonly expanded = input(false);
  /** The id of the suggestion the arrow keys are on, if any. */
  readonly active = input<string>();

  private readonly box = viewChild.required<ElementRef<HTMLInputElement>>('control');

  /**
   * A press on the box beside what is typed (the icon, the margin, a hint) is a press on the
   * field: the focus stays in it, or comes to it. What the screen put inside that can itself
   * be pressed keeps its own.
   */
  protected pressed(event: MouseEvent): void {
    const on = event.target;
    if (on instanceof HTMLInputElement) return;
    if (on instanceof Element && on.closest('button, a')) return;
    event.preventDefault();
    this.focus();
  }

  focus(): void {
    this.box().nativeElement.focus();
  }

  blur(): void {
    this.box().nativeElement.blur();
  }
}
