import { Component, input } from '@angular/core';

/**
 * A figure of the library: what it counts, the figure, and a line under it that says what
 * the figure covers. Its words are the caller's. What is projected (a bar, say) goes between
 * the figure and that line.
 */
@Component({
  selector: 'rs-stat-tile',
  templateUrl: './stat-tile.html',
  styleUrl: './stat-tile.css',
})
export class StatTile {
  readonly caption = input.required<string>();
  readonly figure = input.required<string>();
  readonly note = input<string>();
}
