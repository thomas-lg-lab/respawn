import { TranslocoTestingModule } from '@jsverse/transloco';
import en from '@app/i18n/en.json';
import fr from '@app/i18n/fr.json';
import type { Language } from '@app/i18n/language';

/**
 * For the specs: the real translation files, loaded before the first screen, so a test
 * reads what a person would. Goes in the `imports` of the testing module.
 */
export function testTranslations(language: Language) {
  return TranslocoTestingModule.forRoot({
    langs: { fr, en },
    translocoConfig: {
      availableLangs: ['fr', 'en'],
      defaultLang: language,
      reRenderOnLangChange: true,
    },
    preloadLangs: true,
  });
}
