import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { AccountQueries } from '@respawn/data';
import { Banner, Logo } from '@respawn/ui';
import { type Character, DisplaySettings, type Theme } from '@respawn/util';
import { injectMutation, injectQuery } from '@tanstack/angular-query';
import { AccountMenu } from '@app/components/account-menu/account-menu';
import { failureKey } from '@app/errors/failure-message';
import { type Language, LanguageSetting } from '@app/i18n/language';

/**
 * The frame of the signed-in screens: the header, with the account menu, and the screen
 * under it. It is where the menu's requests meet what answers them: the settings, shown at
 * once and kept on the account, and the sign-out.
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
  protected readonly savingSettings = injectMutation(() => this.queries.saveSettings());

  /** A language is kept only once its translations are there, and shown. */
  protected async pickLanguage(language: Language): Promise<void> {
    if (await this.language.use(language)) this.saveSettings();
  }

  protected pickTheme(theme: Theme): void {
    this.display.setTheme(theme);
    this.saveSettings();
  }

  protected pickCharacter(character: Character): void {
    this.display.setCharacter(character);
    this.saveSettings();
  }

  protected signOut(): void {
    this.signingOut.mutate(undefined, {
      onSuccess: () => void this.router.navigateByUrl('/sign-in'),
    });
  }

  /** The three as the interface shows them: the account holds all of them or none. */
  private saveSettings(): void {
    this.savingSettings.mutate({
      language: this.language.current(),
      theme: this.display.theme(),
      character: this.display.character(),
    });
  }
}
