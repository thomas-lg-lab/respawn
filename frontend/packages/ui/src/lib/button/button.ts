import { Component, ElementRef, effect, inject, input, isDevMode } from '@angular/core';

/**
 * `primary` is the one filled action of a screen, `outlined` every other, `icon` a round
 * button that holds an icon and nothing else.
 */
export type ButtonVariant = 'primary' | 'outlined' | 'icon';

/**
 * `pill` everywhere but in a form, where a button is a `field`: as wide and as round as the
 * fields above it.
 */
export type ButtonShape = 'pill' | 'field';

/**
 * A button, on the native element so its type, its `disabled` and its focus stay the
 * browser's: `<button rsButton>` or, for a navigation, `<a rsButton>`.
 *
 * An `icon` button shows no text, so it must be named, with `aria-label` or
 * `aria-labelledby`. While developing and in tests an unnamed one is reported on the
 * console; it never throws, so a name that arrives late (a value not loaded yet) or a
 * forgotten one cannot take a screen down.
 */
@Component({
  selector: 'button[rsButton], a[rsButton]',
  host: {
    '[attr.data-variant]': 'variant()',
    '[attr.data-shape]': 'shape()',
    '[attr.aria-label]': 'ariaLabel()',
  },
  template: `<ng-content />`,
  styleUrl: './button.css',
})
export class Button {
  readonly variant = input<ButtonVariant>('outlined');
  readonly shape = input<ButtonShape>('pill');
  readonly ariaLabel = input<string | undefined>(undefined, { alias: 'aria-label' });

  constructor() {
    if (isDevMode()) {
      const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
      effect(() => {
        const named = !!this.ariaLabel()?.trim() || host.hasAttribute('aria-labelledby');
        if (this.variant() === 'icon' && !named) {
          console.error(
            'rsButton: this icon button has no accessible name. Give it an aria-label.',
            host,
          );
        }
      });
    }
  }
}
