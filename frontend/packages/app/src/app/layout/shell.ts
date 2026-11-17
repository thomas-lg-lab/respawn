import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { AccountQueries } from '@respawn/data';
import { Logo } from '@respawn/ui';
import { DisplaySettings } from '@respawn/util';
import { injectMutation, injectQuery } from '@tanstack/angular-query';
import { AccountMenu } from '@app/components/account-menu/account-menu';
import { LanguageSetting } from '@app/i18n/language';

/**
 * The frame of the signed-in screens: the header, with the account menu, and the screen
 * under it. It is where the menu's requests meet what answers them: the settings kept in
 * this browser, and the sign-out.
 */
@Component({
  selector: 'rs-shell',
  imports: [RouterLink, RouterOutlet, Logo, AccountMenu],
  templateUrl: './shell.html',
})
export class Shell {
  private readonly queries = inject(AccountQueries);
  private readonly router = inject(Router);

  protected readonly display = inject(DisplaySettings);
  protected readonly language = inject(LanguageSetting);

  protected readonly me = injectQuery(() => this.queries.me());
  protected readonly signingOut = injectMutation(() => this.queries.signOut());

  protected signOut(): void {
    this.signingOut.mutate(undefined, {
      onSuccess: () => void this.router.navigateByUrl('/sign-in'),
    });
  }
}
