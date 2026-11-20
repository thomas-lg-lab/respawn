import { Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Button } from '@respawn/ui';

/** A provider as this list shows it: its label, and the address that starts a sign-in through it. */
export interface ProviderChoice {
  readonly name: string;
  readonly label: string;
  readonly address: string;
}

/**
 * The sign-in providers of the instance, one link each. A sign-in through a provider is a
 * navigation, to the API and on to the provider: these are links, not buttons that call.
 * `separated` puts an "or" above them, under a form of the screen.
 */
@Component({
  selector: 'rs-provider-choices',
  imports: [TranslocoDirective, Button],
  templateUrl: './provider-choices.html',
})
export class ProviderChoices {
  readonly providers = input.required<readonly ProviderChoice[]>();
  readonly separated = input(false);
}
