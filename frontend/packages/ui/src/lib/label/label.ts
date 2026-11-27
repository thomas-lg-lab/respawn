import { Component } from '@angular/core';

/**
 * A section label, on the element that fits the outline (`<h2 rsLabel>`, `<p rsLabel>`).
 * Its face, size and tracking follow the character.
 */
@Component({
  selector: '[rsLabel]',
  template: '<ng-content />',
  styleUrl: './label.css',
})
export class Label {}
