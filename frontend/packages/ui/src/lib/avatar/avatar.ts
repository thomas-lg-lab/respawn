import { Component, computed, input } from '@angular/core';

/**
 * An avatar: the first letter of a name in a disc. A decoration, hidden from screen
 * readers: the name itself is always written beside it.
 */
@Component({
  selector: 'rs-avatar',
  host: {
    'aria-hidden': 'true',
    class:
      'inline-flex size-avatar shrink-0 items-center justify-center rounded-pill bg-accent ' +
      'type-control font-semibold text-on-accent',
  },
  template: `{{ initial() }}`,
})
export class Avatar {
  readonly name = input.required<string>();

  /** The first character as a reader sees it, not half of a pair: an accented letter, an emoji. */
  protected readonly initial = computed(() => {
    const [first] = this.name().trim();
    return first?.toLocaleUpperCase() ?? '';
  });
}
