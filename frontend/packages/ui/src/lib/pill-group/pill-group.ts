import { Component, input } from '@angular/core';

let created = 0;

/**
 * A few pills under one label: the positions of a choice (a language, a theme), or filters
 * that go together. The label names the group, and a screen reader says it before the
 * pills. Which pill is pressed, and what a click does, stay with the screen:
 *
 *     <rs-pill-group [label]="…">
 *       <button rsPill [pressed]="…" (click)="…">…</button>
 *     </rs-pill-group>
 */
@Component({
  selector: 'rs-pill-group',
  host: {
    role: 'group',
    '[attr.aria-labelledby]': 'labelId',
  },
  template: `
    <span class="label" [id]="labelId">{{ label() }}</span>
    <div class="pills"><ng-content /></div>
  `,
  styleUrl: './pill-group.css',
})
export class PillGroup {
  readonly label = input.required<string>();

  protected readonly labelId = `rs-pill-group-${++created}`;
}
