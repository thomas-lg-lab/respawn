import { Component, input, model } from '@angular/core';
import { LucideSearch } from '@lucide/angular';

/**
 * A search field: one line with its icon, which filters what is under it as the visitor
 * types. It shows no label: its name is said to a screen reader (`label`), and the
 * placeholder hints at what can be typed. Both are the screen's words.
 */
@Component({
  selector: 'rs-search-field',
  imports: [LucideSearch],
  templateUrl: './search-field.html',
  styleUrl: './search-field.css',
})
export class SearchField {
  readonly label = input.required<string>();
  readonly placeholder = input('');
  readonly value = model('');
}
