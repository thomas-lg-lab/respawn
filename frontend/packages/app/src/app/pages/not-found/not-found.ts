import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Button, Label, Logo } from '@respawn/ui';

/** An address the front has no screen for. */
@Component({
  selector: 'rs-not-found',
  imports: [RouterLink, TranslocoDirective, Button, Label, Logo],
  template: `
    <main
      class="mx-auto flex min-h-dvh max-w-5xl flex-col items-start justify-center gap-5 px-4 py-10 wide:px-10"
      *transloco="let t; prefix: 'notFound'"
    >
      <rs-logo class="w-48" />
      <div class="flex flex-col gap-2">
        <p rsLabel main>{{ t('label') }}</p>
        <h1 class="type-page-title">{{ t('title') }}</h1>
        <p class="max-w-prose text-muted">{{ t('text') }}</p>
      </div>
      <a rsButton variant="primary" routerLink="/">{{ t('back') }}</a>
    </main>
  `,
})
export class NotFound {}
