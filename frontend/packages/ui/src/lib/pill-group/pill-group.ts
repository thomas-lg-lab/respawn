import { Component, booleanAttribute, input } from '@angular/core';

let created = 0;

/**
 * A few pills under one label: the positions of a choice (a language, a theme), or filters
 * that go together. The label names the group, and a screen reader says it before the
 * pills. Which pill is pressed, and what a click does, stay with the screen:
 *
 *     <rs-pill-group [label]="…">
 *       <button rsPill [pressed]="…" (click)="…">…</button>
 *     </rs-pill-group>
 *
 * `segmented` is for the positions of one choice where room is short: the label and the
 * pills share a line, and the pills (`variant="segment"`) one track.
 */
@Component({
  selector: 'rs-pill-group',
  host: {
    role: 'group',
    '[attr.aria-labelledby]': 'labelId',
    '[attr.data-segmented]': "segmented() ? '' : null",
  },
  templateUrl: './pill-group.html',
  styleUrl: './pill-group.css',
})
export class PillGroup {
  readonly label = input.required<string>();
  readonly segmented = input(false, { transform: booleanAttribute });

  protected readonly labelId = `rs-pill-group-${++created}`;
}
