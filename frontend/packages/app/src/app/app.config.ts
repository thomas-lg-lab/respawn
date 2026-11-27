import {
  ApplicationConfig,
  inject,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withInMemoryScrolling } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { DisplaySettings } from '@respawn/util';
import { routes } from './app.routes';
import { FALLBACK_LANGUAGE, LANGUAGES, LanguageSetting, initialLanguage } from './i18n/language';
import { TranslationLoader } from './i18n/translation-loader';
import { provideAccountSettings } from './session/account-settings';
import { provideServerState } from './session/session.providers';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // The way back finds a screen scrolled where it was left; any other arrival starts at its top.
    provideRouter(routes, withInMemoryScrolling({ scrollPositionRestoration: 'enabled' })),
    // Server state: what the API answered, kept and shared between screens. Each feature
    // says how in its `<feature>.queries.ts`.
    provideServerState(),
    // The settings of the interface follow the account the server state says is signed in.
    provideAccountSettings(),
    provideTransloco({
      config: {
        availableLangs: [...LANGUAGES],
        defaultLang: initialLanguage(),
        fallbackLang: FALLBACK_LANGUAGE,
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
      },
      loader: TranslationLoader,
    }),
    // Before the first screen: the theme and the character on the root element, and the
    // translations of the starting language.
    provideAppInitializer(() => {
      inject(DisplaySettings);
      return inject(LanguageSetting).start();
    }),
  ],
};
