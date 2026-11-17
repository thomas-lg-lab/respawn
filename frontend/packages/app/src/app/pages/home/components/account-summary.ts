import { Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { Account } from '@respawn/data';
import { Card, Label } from '@respawn/ui';

/** Who is signed in: the name, the e-mail address when the account has one, and the role. */
@Component({
  selector: 'rs-account-summary',
  imports: [TranslocoDirective, Card, Label],
  template: `
    <div class="flex flex-col gap-4.5" *transloco="let t; prefix: 'home'">
      <div class="flex flex-col gap-2">
        <p rsLabel main>{{ t('label') }}</p>
        <h1 class="type-page-title">{{ t('title', { name: account().display_name }) }}</h1>
      </div>

      <rs-card>
        <dl class="grid grid-cols-[auto_1fr] gap-x-6 gap-y-3">
          @if (account().email; as email) {
            <dt class="text-muted">{{ t('email') }}</dt>
            <dd class="min-w-0 break-words" data-account-email>{{ email }}</dd>
          }
          <dt class="text-muted">{{ t('role') }}</dt>
          <dd data-account-role>{{ t(account().is_admin ? 'administrator' : 'member') }}</dd>
        </dl>
      </rs-card>
    </div>
  `,
})
export class AccountSummary {
  readonly account = input.required<Account>();
}
