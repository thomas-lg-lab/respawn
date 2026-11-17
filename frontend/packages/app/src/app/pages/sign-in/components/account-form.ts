import { Component, input, output, signal } from '@angular/core';
import {
  FormField,
  FormRoot,
  email,
  form,
  maxLength,
  minLength,
  required,
} from '@angular/forms/signals';
import { TranslocoDirective } from '@jsverse/transloco';
import type { RegisterRequest } from '@respawn/data';
import { Button, TextField } from '@respawn/ui';
import { fieldErrorKey } from '@app/forms/field-error';

/**
 * The form of a new local account: a display name, an e-mail address and a password, whose
 * one rule (a minimum length, the instance's) is written under its field before anything is
 * typed. It checks what can be checked here, then hands the values over; creating the
 * account, and saying that it was refused, is the screen's.
 */
@Component({
  selector: 'rs-account-form',
  imports: [TranslocoDirective, FormField, FormRoot, Button, TextField],
  template: `
    <form class="flex flex-col gap-4" [formRoot]="form" *transloco="let t">
      @let nameError = fieldErrorKey(form.display_name().errors());
      <rs-text-field
        autocomplete="name"
        [formField]="form.display_name"
        [label]="t('createAccount.displayName')"
        [error]="nameError && t(nameError)"
      />

      @let emailError = fieldErrorKey(form.email().errors());
      <rs-text-field
        type="email"
        autocomplete="username"
        [formField]="form.email"
        [label]="t('signIn.email')"
        [error]="emailError && t(emailError)"
      />

      @let passwordError = fieldErrorKey(form.password().errors());
      <rs-text-field
        type="password"
        autocomplete="new-password"
        [formField]="form.password"
        [label]="t('signIn.password')"
        [hint]="t('createAccount.passwordRule', { count: passwordMinLength() })"
        [error]="passwordError && t(passwordError, { count: passwordMinLength() })"
      />

      <button rsButton variant="primary" shape="field" type="submit" [disabled]="pending()">
        {{ t('createAccount.submit') }}
      </button>
    </form>
  `,
})
export class AccountForm {
  /** The shortest password the instance accepts. */
  readonly passwordMinLength = input.required<number>();
  /** While the account is being created: the button waits. */
  readonly pending = input(false);
  readonly submitted = output<RegisterRequest>();

  protected readonly fieldErrorKey = fieldErrorKey;

  private readonly model = signal<RegisterRequest>({ display_name: '', email: '', password: '' });

  // The maximum lengths are the API's own limits; a longer value could only be refused.
  protected readonly form = form(
    this.model,
    (path) => {
      required(path.display_name);
      maxLength(path.display_name, 100);
      required(path.email);
      email(path.email);
      maxLength(path.email, 320);
      required(path.password);
      minLength(path.password, () => this.passwordMinLength());
      maxLength(path.password, 256);
    },
    {
      submission: {
        // Reached only with every rule met. Nothing to wait for: the screen takes over.
        action: () => {
          this.submitted.emit(this.model());
          return Promise.resolve();
        },
        onInvalid: (field) => field().errorSummary()[0]?.fieldTree().focusBoundControl(),
      },
    },
  );
}
