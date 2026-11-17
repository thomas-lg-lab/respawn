import { Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { Account } from '@respawn/data';
import { Card, Label } from '@respawn/ui';

/** Who is signed in: the name, the e-mail address when the account has one, and the role. */
@Component({
  selector: 'rs-account-summary',
  imports: [TranslocoDirective, Card, Label],
  templateUrl: './account-summary.html',
})
export class AccountSummary {
  readonly account = input.required<Account>();
}
