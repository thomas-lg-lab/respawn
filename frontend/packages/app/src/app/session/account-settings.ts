import {
  type EnvironmentProviders,
  Service,
  inject,
  provideEnvironmentInitializer,
} from '@angular/core';
import { type Account, accountKeys } from '@respawn/data';
import { CHARACTERS, DisplaySettings, THEMES } from '@respawn/util';
import { QueryClient, hashKey } from '@tanstack/angular-query';
import { LANGUAGES, type Language, LanguageSetting } from '@app/i18n/language';

/**
 * The settings of the interface follow the account. Whenever the server state learns an
 * account's settings that the interface has not taken yet (a sign-in, the app opened with a
 * session, a change made on another device and read when the window is back in front), they
 * become the interface's, and this browser keeps them for its next visit.
 *
 * The answer to a save made here is not news: the interface already shows what it saved, and
 * taking it again could undo a later pick whose own save is still on its way. An account that
 * never chose leaves the browser's own choices as they are.
 */
@Service()
export class AccountSettingsFollower {
  private readonly client = inject(QueryClient);
  private readonly display = inject(DisplaySettings);
  private readonly language = inject(LanguageSetting);

  /** The account and its settings last taken, so that each is taken once. */
  private taken: string | null = null;

  start(): void {
    const me = hashKey(accountKeys.me);
    this.client.getQueryCache().subscribe((event) => {
      if (
        event.type === 'updated' &&
        event.action.type === 'success' &&
        event.query.queryHash === me
      ) {
        this.follow(event.query.state.data as Account | null | undefined);
      }
    });
    this.follow(this.client.getQueryData<Account | null>(accountKeys.me));
  }

  private follow(account: Account | null | undefined): void {
    const settings = account?.settings;
    const seen = settings
      ? [account.id, settings.language, settings.theme, settings.character].join(' ')
      : null;
    if (seen === this.taken) return;
    this.taken = seen;
    if (!settings || this.client.isMutating({ mutationKey: accountKeys.saveSettings }) > 0) return;

    // Only words the interface knows: an API newer than this tab may say more.
    const language = LANGUAGES.find((each) => each === settings.language);
    const theme = THEMES.find((each) => each === settings.theme);
    const character = CHARACTERS.find((each) => each === settings.character);
    if (theme) this.display.setTheme(theme);
    if (character) this.display.setCharacter(character);
    if (language) void this.retakeIfUnavailable(language, seen);
  }

  /**
   * A language whose file could not be fetched was not taken: the next answer that brings
   * the same settings tries again. One the user picked meanwhile is left as it is.
   */
  private async retakeIfUnavailable(language: Language, seen: string | null): Promise<void> {
    if ((await this.language.use(language)) === 'unavailable' && this.taken === seen) {
      this.taken = null;
    }
  }
}

/** Starts following the account's settings, beside the server state it reads. */
export function provideAccountSettings(): EnvironmentProviders {
  return provideEnvironmentInitializer(() => inject(AccountSettingsFollower).start());
}
