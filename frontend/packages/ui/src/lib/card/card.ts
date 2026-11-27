import { Component, booleanAttribute, input } from '@angular/core';

/**
 * A card: a surface with a hairline border. No shadow anywhere: surfaces are told apart by
 * their fill and by that line. `interactive`: the whole card is a link, and it fills on hover
 * like every target of the app that is more than a line of text.
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
