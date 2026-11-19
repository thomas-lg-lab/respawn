import { DOCUMENT, Service, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { readChoice, writeChoice } from '@respawn/util';
import { firstValueFrom } from 'rxjs';

/** The languages the app is written in. */
export const LANGUAGES = ['fr', 'en'] as const;
export type Language = (typeof LANGUAGES)[number];

/** Shown to a browser that asks for neither. */
export const FALLBACK_LANGUAGE: Language = 'en';

const LANGUAGE_KEY = 'respawn.language';

/**
 * The first of the browser's languages the app is written in, in the browser's own order
 * of preference: `fr-CA` gets French, `de, en-GB` gets English.
 */
export function browserLanguage(preferred: readonly string[]): Language {
  for (const tag of preferred) {
    const primary = tag.toLowerCase().split('-')[0];
    const match = LANGUAGES.find((language) => language === primary);
    if (match) return match;
  }
  return FALLBACK_LANGUAGE;
}

/** The language to start in: the one chosen in this browser, else the browser's. */
export function initialLanguage(): Language {
  return readChoice(LANGUAGE_KEY, LANGUAGES) ?? browserLanguage(navigator.languages);
}

/**
 * The language of the interface. Before sign-in it is the browser's, or the one chosen
 * here, which this browser keeps; after it, the account's, when the account chose one.
 */
@Service()
export class LanguageSetting {
  private readonly transloco = inject(TranslocoService);
  private readonly root = inject(DOCUMENT).documentElement;

  readonly current = signal<Language>(initialLanguage());

  /**
   * Loads the starting language before the first screen is drawn: no flash of keys. It never
   * fails: a language whose file cannot be fetched (offline, or a tab older than the version
   * now served) gives way to the fallback, and with no file at all the app still starts.
   * The kept choice is left alone: it was not at fault, and the next visit tries it again.
   */
  async start(): Promise<void> {
    const wanted = this.current();
    if (await this.load(wanted)) {
      this.show(wanted);
    } else if (wanted !== FALLBACK_LANGUAGE && (await this.load(FALLBACK_LANGUAGE))) {
      this.show(FALLBACK_LANGUAGE);
    }
  }

  /**
   * Switches to a language once its translations are there, and only then remembers it.
   * Answers whether it switched: when the file cannot be fetched, nothing changes.
   */
  async use(language: Language): Promise<boolean> {
    if (!(await this.load(language))) return false;
    writeChoice(LANGUAGE_KEY, language);
    this.show(language);
    return true;
  }

  /** Whether the language's own translations are loaded. */
  private async load(language: Language): Promise<boolean> {
    try {
      // Completes without a value when Transloco answers a failed load from a fallback it
      // already holds.
      await firstValueFrom(this.transloco.load(language), { defaultValue: null });
    } catch {
      return false;
    }
    // After a failed load Transloco quietly holds the fallback's translations instead:
    // only the language's own count.
    return Object.keys(this.transloco.getTranslation(language)).length > 0;
  }

  private show(language: Language): void {
    this.transloco.setActiveLang(language);
    this.root.lang = language;
    this.current.set(language);
  }
}
