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
    class: 'flex flex-col gap-1.5',
    '[attr.aria-labelledby]': 'labelId',
  },
  template: `
    <span class="type-label text-muted" [id]="labelId">{{ label() }}</span>
    <div class="flex flex-wrap gap-1.5"><ng-content /></div>
  `,
})
export class PillGroup {
  readonly label = input.required<string>();

  protected readonly labelId = `rs-pill-group-${++created}`;
}
