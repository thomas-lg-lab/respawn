import { Service } from '@angular/core';
import type { Translation, TranslocoLoader } from '@jsverse/transloco';
import type { Language } from './language';

/**
 * Each language is a chunk of the build, fetched when it is first used: the file name
 * carries a hash like every other chunk, so a new version never reads stale translations.
 */
const FILES: Record<Language, () => Promise<{ default: Translation }>> = {
  fr: () => import('./fr.json'),
  en: () => import('./en.json'),
};

@Service()
export class TranslationLoader implements TranslocoLoader {
  async getTranslation(language: string): Promise<Translation> {
    const file = FILES[language as Language];
    return (await file()).default;
  }
}
