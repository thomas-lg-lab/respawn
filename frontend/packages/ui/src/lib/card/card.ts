import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input,
} from '@angular/core';

/**
 * A card: a surface with a hairline border. No shadow anywhere: surfaces are told apart by
 * their fill and by that line. `interactive` adds the hover lift.
 */
@Component({
  selector: 'rs-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class]': 'classes()' },
  template: `<ng-content />`,
})
export class Card {
  readonly interactive = input(false, { transform: booleanAttribute });

  protected readonly classes = computed(
    () =>
      'block rounded-card border border-line bg-surface p-5 wide:p-6' +
      (this.interactive() ? ' transition-[translate] duration-hover hover:-translate-y-lift' : ''),
  );
}
