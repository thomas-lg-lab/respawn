import { Component, effect, inject } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { AccountQueries } from '@respawn/data';
import { Banner, Logo } from '@respawn/ui';
import { DisplaySettings } from '@respawn/util';
import { injectMutation, injectQuery } from '@tanstack/angular-query';
import { AccountMenu } from '@app/components/account-menu/account-menu';
import { failureKey } from '@app/errors/failure-message';
import { LanguageSetting } from '@app/i18n/language';

/**
 * The frame of the signed-in screens: the header, with the account menu, and the screen
 * under it. It is where the menu's requests meet what answers them: the settings kept in
 * this browser, and the sign-out.
 *
 * It is also the way out: as soon as nobody is signed in (a sign-out, or a session that
 * ended on its own, which the server state learns from the API), it leads to the sign-in.
 */
@Component({
  selector: 'rs-shell',
  imports: [RouterLink, RouterOutlet, TranslocoDirective, Banner, Logo, AccountMenu],
  templateUrl: './shell.html',
})
export class Shell {
  private readonly queries = inject(AccountQueries);
  private readonly router = inject(Router);

  protected readonly display = inject(DisplaySettings);
  protected readonly language = inject(LanguageSetting);
  protected readonly failureKey = failureKey;

  protected readonly me = injectQuery(() => this.queries.me());
  protected readonly signingOut = injectMutation(() => this.queries.signOut());

  constructor() {
    effect(() => {
      if (this.me.data() === null) void this.router.navigateByUrl('/sign-in');
    });
  }

  protected signOut(): void {
    this.signingOut.mutate();
  }
}
