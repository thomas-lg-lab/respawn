import { Component, computed, input, output, signal } from '@angular/core';
import { FormField, FormRoot, form, maxLength } from '@angular/forms/signals';
import { TranslocoDirective } from '@jsverse/transloco';
import { Button, TextField } from '@respawn/ui';
import { fieldErrorKey } from '@app/forms/field-error';

/**
 * The owner's own key of a store, for one linked account: a field to give one, or to put
 * another in place of the one the account has, and the way to take it away. The key the
 * account has is never shown: the API does not answer it.
 *
 * It hands the key over and forgets it at once; keeping it, and saying that it was refused,
 * is the screen's.
 */
@Component({
  selector: 'rs-own-key-form',
  imports: [TranslocoDirective, FormField, FormRoot, Button, TextField],
  templateUrl: './own-key-form.html',
})
export class OwnKeyForm {
  /** Whether the account has a key of its owner's already. */
  readonly hasKey = input.required<boolean>();
  /** While a key is being kept or taken away: the buttons wait. */
  readonly pending = input(false);
  readonly given = output<string>();
  readonly removed = output<void>();

  protected readonly fieldErrorKey = fieldErrorKey;

  private readonly model = signal({ key: '' });

  /**
   * The key as it would be sent. None is no mistake: the field is not one to fill, and
   * leaving it empty marks nothing in error; the button only waits for a key.
   */
  protected readonly typed = computed(() => this.model().key.trim());

  // The maximum length is the API's own limit; a longer value could only be refused.
  protected readonly form = form(
    this.model,
    (path) => {
      maxLength(path.key, 256);
    },
    {
      submission: {
        // Reached only with every rule met. Nothing to wait for: the screen takes over, and
        // the key does not stay in the page.
        action: () => {
          const key = this.typed();
          if (key) {
            this.given.emit(key);
            this.form().reset({ key: '' });
          }
          return Promise.resolve();
        },
        onInvalid: (field) => field().errorSummary()[0]?.fieldTree().focusBoundControl(),
      },
    },
  );
}
