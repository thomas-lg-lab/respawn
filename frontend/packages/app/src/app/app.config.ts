import {
  ApplicationConfig,
  inject,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideTransloco } from '@jsverse/transloco';
import { DisplaySettings } from '@respawn/util';
import { FALLBACK_LANGUAGE, LANGUAGES, LanguageSetting, initialLanguage } from './i18n/language';
import { TranslationLoader } from './i18n/translation-loader';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
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
