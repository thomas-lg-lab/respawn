import { Component, booleanAttribute, input } from '@angular/core';

/**
 * A card: a surface with a hairline border. No shadow anywhere: surfaces are told apart by
 * their fill and by that line. `interactive` adds the hover lift.
 */
@Component({
  selector: 'rs-card',
  host: { '[attr.data-interactive]': "interactive() ? '' : null" },
  template: `<ng-content />`,
  styleUrl: './card.css',
})
export class Card {
  readonly interactive = input(false, { transform: booleanAttribute });
}
