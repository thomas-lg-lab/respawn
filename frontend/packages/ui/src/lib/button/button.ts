import { Component, computed, input } from '@angular/core';

/**
 * `primary` is the one filled action of a screen, `outlined` every other, `icon` a round
 * button that holds an icon and nothing else.
 */
export type ButtonVariant = 'primary' | 'outlined' | 'icon';

const BASE =
  'inline-flex shrink-0 items-center justify-center gap-2 rounded-pill type-control ' +
  'transition-[background-color,opacity,translate] duration-hover ' +
  'active:translate-y-px disabled:pointer-events-none disabled:opacity-50';

const VARIANTS: Record<ButtonVariant, string> = {
  primary: 'min-h-button bg-accent px-5.5 font-semibold text-on-accent hover:opacity-90',
  outlined:
    'min-h-touch border border-control-line bg-transparent px-4.5 font-medium text-text hover:bg-surface-2',
  icon: 'size-touch border border-control-line bg-transparent text-text hover:bg-surface-2',
};

/**
 * A button, on the native element so its type, its `disabled` and its focus stay the
 * browser's: `<button rsButton>` or, for a navigation, `<a rsButton>`.
 *
 * An `icon` button shows no text, so it must be named: `aria-label` is required there, and
 * a missing one is an error rather than a silent unnamed control.
 */
@Component({
  selector: 'button[rsButton], a[rsButton]',
  host: { '[class]': 'classes()', '[attr.aria-label]': 'ariaLabel()' },
  template: `<ng-content />`,
})
export class Button {
  readonly variant = input<ButtonVariant>('outlined');
  readonly ariaLabel = input<string | undefined>(undefined, { alias: 'aria-label' });

  protected readonly classes = computed(() => {
    const variant = this.variant();
    if (variant === 'icon' && !this.ariaLabel()?.trim()) {
      throw new Error('rsButton: an icon button needs an aria-label, it shows no text.');
    }
    return `${BASE} ${VARIANTS[variant]}`;
  });
}
