import { Location } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  type IsActiveMatchOptions,
  NavigationEnd,
  Router,
  RouterLink,
  RouterLinkActive,
  RouterOutlet,
} from '@angular/router';
import { filter } from 'rxjs';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  AccountQueries,
  GameQueries,
  LibraryQueries,
  LinkedAccountQueries,
  PROVIDER_OUTCOME,
  providerOutcome,
  providerSignInAddress,
} from '@respawn/data';
import { Banner, Logo, Tab } from '@respawn/ui';
import { type Character, DisplaySettings, type Theme } from '@respawn/util';
import { injectMutation, injectQuery } from '@tanstack/angular-query';
import { AccountMenu, type LinkableProvider } from '@app/components/account-menu/account-menu';
import { GameSearch } from '@app/components/game-search/game-search';
import { failureKey } from '@app/errors/failure-message';
import { LIBRARY_SEARCH } from '@app/games/game-list';
import { cameFrom, gamePage, originOf } from '@app/games/game-page';
import { type Language, LanguageSetting } from '@app/i18n/language';
import { saidOnce, withoutSaidOnce } from '@app/session/said-once';

/** The screens a tab leads to, in the order of the tabs, each with the key of its name. */
const TABS = [
  { path: '/', label: 'nav.home' },
  { path: '/library', label: 'nav.library' },
  { path: '/time', label: 'nav.time' },
] as const;

/**
 * A tab is the current one on its own address, whatever a return from a provider or from
 * Steam wrote after it.
 */
const SAME_PATH: IsActiveMatchOptions = {
  paths: 'exact',
  queryParams: 'ignored',
  matrixParams: 'ignored',
  fragment: 'ignored',
};

/**
 * The frame of the signed-in screens: the header, with the tabs, the search and the account
 * menu, and the screen under it; on a phone the tabs are a bar at the foot of the window. It
 * is where the menu's requests meet what answers them: the settings, shown at once and kept
 * on the account, the providers to link, and the sign-out; and where a title that was
 * searched leads to the Library, and a game chosen in the search to its page. A link through a
 * provider comes back to the screen it started from, and what came of it is said here.
 *
 * It also keeps the linked accounts in view, whatever the screen: that is where the end of a
 * sync is learnt, and the library a screen shows must not wait for the home to learn it.
 */
@Component({
  selector: 'rs-shell',
  imports: [
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    TranslocoDirective,
    Banner,
    Logo,
    Tab,
    AccountMenu,
    GameSearch,
  ],
  // As tall as the window at least, so the bar of tabs has a foot to stick to.
  host: { class: 'flex min-h-dvh flex-col' },
  templateUrl: './shell.html',
})
export class Shell {
  private readonly queries = inject(AccountQueries);
  private readonly linkedAccounts = inject(LinkedAccountQueries);
  private readonly gameQueries = inject(GameQueries);
  private readonly library = inject(LibraryQueries);
  private readonly router = inject(Router);

  protected readonly display = inject(DisplaySettings);
  protected readonly language = inject(LanguageSetting);
  protected readonly failureKey = failureKey;
  protected readonly tabs = TABS;
  protected readonly samePath = SAME_PATH;

  protected readonly me = injectQuery(() => this.queries.me());
  protected readonly signingOut = injectMutation(() => this.queries.signOut());
  protected readonly savingSettings = injectMutation(() => this.queries.saveSettings());
  /** The providers, and which ones the account is linked to, asked once the menu that shows them is opened. */
  protected readonly menuOpened = signal(false);
  protected readonly accountProviders = injectQuery(() => ({
    ...this.queries.providers(),
    enabled: this.menuOpened(),
  }));
  /** The sources the instance credits, said in the same menu: asked with it. */
  protected readonly credits = injectQuery(() => ({
    ...this.gameQueries.credits(),
    enabled: this.menuOpened(),
  }));

  /**
   * The games the search suggests from, asked once its field is first used: a screen that
   * shows the library has read them already, and they are not asked twice.
   */
  protected readonly searchOpened = signal(false);
  protected readonly games = injectQuery(() => ({
    ...this.library.games(),
    enabled: this.searchOpened(),
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
   * screen, without what its address said for one arrival (the word of a previous link).
   */
  protected readonly providers = computed<LinkableProvider[]>(() => {
    const back = withoutSaidOnce(this.router, this.address());
    return (this.accountProviders.data() ?? []).map((provider) => ({
      ...provider,
      address: providerSignInAddress(provider.name, back, true),
    }));
  });

  constructor() {
    saidOnce(inject(Location), this.router, PROVIDER_OUTCOME);

    // Watched here and shown by the screens that need them: asked again when the window is
    // back in front and while a sync is awaited, on the library and the time map too.
    injectQuery(() => this.linkedAccounts.list());

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

  /**
   * A title was asked for: the Library looks for it, and says so on its address. Asked again
   * for the title it already shows, it still hears it, and shows every game that bears it.
   */
  protected search(title: string): void {
    void this.router.navigate(['/library'], {
      queryParams: { [LIBRARY_SEARCH]: title },
      onSameUrlNavigation: 'reload',
    });
  }

  /**
   * A game was chosen in the search: its page, which leads back to the screen it was chosen
   * on. Chosen on a screen the page does not lead back to (another game's page, the account),
   * it leads to the library.
   */
  protected openGame(gameId: string): void {
    const here = originOf(this.router.url);
    void this.router.navigate(gamePage(gameId), here ? { state: cameFrom(here) } : {});
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
