import {
  ApplicationConfig,
  inject,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { serverState } from '@respawn/data';
import { DisplaySettings } from '@respawn/util';
import { provideTanStackQuery } from '@tanstack/angular-query';
import { routes } from './app.routes';
import { FALLBACK_LANGUAGE, LANGUAGES, LanguageSetting, initialLanguage } from './i18n/language';
import { TranslationLoader } from './i18n/translation-loader';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    // Server state: what the API answered, kept and shared between screens. Each feature
    // says how in its `<feature>.queries.ts`.
    provideTanStackQuery(serverState),
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
