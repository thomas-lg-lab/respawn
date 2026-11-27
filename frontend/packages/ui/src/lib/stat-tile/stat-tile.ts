import { Component, booleanAttribute, input } from '@angular/core';
import { LucideChevronRight } from '@lucide/angular';

/**
 * A figure of the library: what it counts, the figure, and a line under it that says what
 * the figure covers. Its words are the caller's. What is projected (a bar, say) goes between
 * the figure and that line. `interactive`: the tile is a link's whole box (the caller writes
 * the link around it); it fills on hover, and a chevron in its corner says it leads
 * somewhere, to a finger too.
 */
@Component({
  selector: 'rs-stat-tile',
  imports: [LucideChevronRight],
  host: { '[attr.data-interactive]': "interactive() ? '' : null" },
  templateUrl: './stat-tile.html',
  styleUrl: './stat-tile.css',
})
export class StatTile {
  readonly caption = input.required<string>();
  readonly figure = input.required<string>();
  readonly note = input<string>();
  readonly interactive = input(false, { transform: booleanAttribute });
}
