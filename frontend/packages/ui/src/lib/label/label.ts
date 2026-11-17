import { Component, booleanAttribute, input } from '@angular/core';

/**
 * A section label, on the element that fits the outline (`<h2 rsLabel>`, `<p rsLabel>`).
 * Its face, size and tracking follow the character. `main` marks the main label of a
 * screen: in the `pixel` character a prompt blinks before it. The prompt is decoration,
 * kept away from screen readers.
 */
@Component({
  selector: '[rsLabel]',
  templateUrl: './label.html',
  styleUrl: './label.css',
})
export class Label {
  readonly main = input(false, { transform: booleanAttribute });
}
