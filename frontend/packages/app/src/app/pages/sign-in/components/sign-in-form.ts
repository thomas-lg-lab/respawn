import { Component, input, output, signal } from '@angular/core';
import { FormField, FormRoot, email, form, maxLength, required } from '@angular/forms/signals';
import { TranslocoDirective } from '@jsverse/transloco';
import type { LoginRequest } from '@respawn/data';
import { Button, TextField } from '@respawn/ui';
import { fieldErrorKey } from '@app/forms/field-error';

/**
 * The form of a local sign-in: an e-mail address and a password. It checks what can be
 * checked here, then hands the values over; signing in, and saying that it was refused, is
 * the screen's.
 */
@Component({
  selector: 'rs-sign-in-form',
  imports: [TranslocoDirective, FormField, FormRoot, Button, TextField],
  templateUrl: './sign-in-form.html',
})
export class SignInForm {
  /** While the sign-in is on its way: the button waits. */
  readonly pending = input(false);
  readonly submitted = output<LoginRequest>();

  protected readonly fieldErrorKey = fieldErrorKey;

  private readonly model = signal<LoginRequest>({ email: '', password: '' });

  // The lengths are the API's own limits; a longer value could only be refused.
  protected readonly form = form(
    this.model,
    (path) => {
      required(path.email);
      email(path.email);
      maxLength(path.email, 320);
      required(path.password);
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
