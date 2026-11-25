import { Component, computed, input } from '@angular/core';

/**
 * An avatar: the first letter of a name in a square. A decoration, hidden from screen
 * readers: the name itself is always written beside it.
 */
@Component({
  selector: 'rs-avatar',
  host: { 'aria-hidden': 'true' },
  template: `{{ initial() }}`,
  styleUrl: './avatar.css',
})
export class Avatar {
  readonly name = input.required<string>();

  /** The first character as a reader sees it, not half of a pair: an accented letter, an emoji. */
  protected readonly initial = computed(() => {
    const [first] = new Intl.Segmenter(undefined, { granularity: 'grapheme' }).segment(
      this.name().trim(),
    );
    return first?.segment.toLocaleUpperCase() ?? '';
  });
}
