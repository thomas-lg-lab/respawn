import { Component, ElementRef, computed, input, model, output, viewChild } from '@angular/core';
import type { FormValueControl } from '@angular/forms/signals';

/** What a text field holds, which decides the keyboard a phone shows and what a browser offers. */
export type TextFieldType = 'text' | 'email' | 'password';

let created = 0;

/**
 * A text field: its label above, the input, then one line under it: a hint or, when the
 * value is refused, the reason, which takes the hint's place (a rule and the reminder of
 * that rule would say the same thing twice). It is a control of a signal form
 * (`[formField]`), which gives it its value and its state; the words come from the screen:
 * the label, the hint, and the error to show.
 *
 * The error is drawn once the field was left or the form submitted, never while a first
 * value is being typed. The line shown is tied to the input with `aria-describedby`, and an
 * input in error is marked `aria-invalid`.
 */
@Component({
  selector: 'rs-text-field',
  host: { class: 'flex flex-col gap-1.5' },
  template: `
    <label class="type-control font-medium text-text" [for]="id">{{ label() }}</label>
    <input
      #control
      class="h-button w-full rounded-field border bg-surface px-3.5 text-text disabled:opacity-50"
      [class]="showsError() ? 'border-2 border-warn' : 'border-control-line'"
      [id]="id"
      [type]="type()"
      [attr.name]="name() || null"
      [attr.autocomplete]="autocomplete()"
      [attr.aria-invalid]="showsError() ? 'true' : null"
      [attr.aria-describedby]="describedBy()"
      [required]="required()"
      [disabled]="disabled()"
      [value]="value()"
      (input)="value.set(control.value)"
      (blur)="touch.emit()"
    />
    @if (showsError()) {
      <span class="type-caption text-warn" [id]="errorId">{{ error() }}</span>
    } @else if (hint()) {
      <span class="type-caption text-muted" [id]="hintId">{{ hint() }}</span>
    }
  `,
})
export class TextField implements FormValueControl<string> {
  readonly label = input.required<string>();
  readonly type = input<TextFieldType>('text');
  readonly autocomplete = input<string>();
  readonly hint = input<string | null>(null);
  /** Why the value is refused, in the reader's words. Shown only while the field is invalid. */
  readonly error = input<string | null>(null);

  // What the form gives the control.
  readonly value = model('');
  readonly name = input('');
  readonly required = input(false);
  readonly disabled = input(false);
  readonly invalid = input(false);
  readonly touched = input(false);
  // No `maxLength` here on purpose: as an attribute of the input it would cut a pasted value
  // short without a word (a password, say). The form's rule refuses a value too long instead.
  readonly touch = output<void>();

  private readonly control = viewChild.required<ElementRef<HTMLInputElement>>('control');

  protected readonly id = `rs-text-field-${++created}`;
  protected readonly hintId = `${this.id}-hint`;
  protected readonly errorId = `${this.id}-error`;

  protected readonly showsError = computed(
    () => this.invalid() && this.touched() && !!this.error(),
  );

  protected readonly describedBy = computed(() => {
    if (this.showsError()) return this.errorId;
    return this.hint() ? this.hintId : null;
  });

  /** Where the form sends the focus when it refuses a submission. */
  focus(options?: FocusOptions): void {
    this.control().nativeElement.focus(options);
  }
}
