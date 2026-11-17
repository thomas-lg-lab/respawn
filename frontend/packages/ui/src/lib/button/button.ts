import { Component, ElementRef, computed, effect, inject, input, isDevMode } from '@angular/core';

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

const BASE =
  'inline-flex shrink-0 items-center justify-center gap-2 type-control ' +
  'transition-[background-color,opacity,translate] duration-hover ' +
  'active:translate-y-px disabled:pointer-events-none disabled:opacity-50';

const VARIANTS: Record<ButtonVariant, string> = {
  primary: 'min-h-button bg-accent px-5.5 font-semibold text-on-accent hover:opacity-90',
  outlined:
    'min-h-touch border border-control-line bg-transparent px-4.5 font-medium text-text hover:bg-surface-2',
  icon: 'size-touch border border-control-line bg-transparent text-text hover:bg-surface-2',
};

const SHAPES: Record<ButtonShape, string> = {
  pill: 'rounded-pill',
  field: 'w-full rounded-field',
};

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
  host: { '[class]': 'classes()', '[attr.aria-label]': 'ariaLabel()' },
  template: `<ng-content />`,
})
export class Button {
  readonly variant = input<ButtonVariant>('outlined');
  readonly ariaLabel = input<string | undefined>(undefined, { alias: 'aria-label' });

  readonly shape = input<ButtonShape>('pill');

  protected readonly classes = computed(
    () => `${BASE} ${SHAPES[this.shape()]} ${VARIANTS[this.variant()]}`,
  );

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
