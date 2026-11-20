import { Location } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  AccountQueries,
  PROVIDER_OUTCOME,
  providerOutcome,
  providerSignInAddress,
} from '@respawn/data';
import { Banner, Logo } from '@respawn/ui';
import { type Character, DisplaySettings, type Theme } from '@respawn/util';
import { injectMutation, injectQuery } from '@tanstack/angular-query';
import { AccountMenu, type LinkableProvider } from '@app/components/account-menu/account-menu';
import { failureKey } from '@app/errors/failure-message';
import { type Language, LanguageSetting } from '@app/i18n/language';
import { saidOnce } from '@app/session/said-once';

/**
 * The frame of the signed-in screens: the header, with the account menu, and the screen
 * under it. It is where the menu's requests meet what answers them: the settings, shown at
 * once and kept on the account, the providers to link, and the sign-out. A link through a
 * provider comes back to the screen it started from, and what came of it is said here.
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
  /** The providers, and which ones the account is linked to, asked once the menu that shows them is opened. */
  protected readonly menuOpened = signal(false);
  protected readonly accountProviders = injectQuery(() => ({
    ...this.queries.providers(),
    enabled: this.menuOpened(),
  }));

  /** The address of the screen, as the last navigation left it. */
  private readonly address = signal(this.router.url);

  /**
   * What came of a link through a provider, as the API wrote it on the address it came back to.
   * Said until the visitor moves on, and once: the word is taken off the browser's address.
   */
  protected readonly cameBack = computed(() =>
    providerOutcome(this.router.parseUrl(this.address()).queryParamMap.get(PROVIDER_OUTCOME)),
  );

  /**
   * The providers: the ones linked already, and the others to link, each coming back to this
   * screen, without the word of a previous link.
   */
  protected readonly providers = computed<LinkableProvider[]>(() => {
    const here = this.router.parseUrl(this.address());
    here.queryParams = Object.fromEntries(
      Object.entries(here.queryParams).filter(([key]) => key !== PROVIDER_OUTCOME),
    );
    const back = this.router.serializeUrl(here);
    return (this.accountProviders.data() ?? []).map((provider) => ({
      ...provider,
      address: providerSignInAddress(provider.name, back, true),
    }));
  });

  constructor() {
    saidOnce(inject(Location), this.router, PROVIDER_OUTCOME);

    // A warning about a pick speaks of the screen it was made on: the next one starts clear.
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe((event) => {
        this.savingSettings.reset();
        this.address.set(event.urlAfterRedirects);
      });
  }

  /** A language is kept only once its translations are there, and shown. */
  protected async pickLanguage(language: Language): Promise<void> {
    if ((await this.language.use(language)) === 'shown') this.saveSettings();
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

  /**
   * The three as the interface shows them, or is about to for a language still loading: the
   * account holds all of them or none.
   */
  private saveSettings(): void {
    this.savingSettings.mutate({
      language: this.language.chosen(),
      theme: this.display.theme(),
      character: this.display.character(),
    });
  }
}
