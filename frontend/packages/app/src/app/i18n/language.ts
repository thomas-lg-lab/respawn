import { DOCUMENT, Injectable, inject, signal } from '@angular/core';
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
 * here, which this browser keeps; an account will carry it later.
 */
@Injectable({ providedIn: 'root' })
export class LanguageSetting {
  private readonly transloco = inject(TranslocoService);
  private readonly root = inject(DOCUMENT).documentElement;

  readonly current = signal<Language>(initialLanguage());

  /** Loads the starting language before the first screen is drawn: no flash of keys. */
  async start(): Promise<void> {
    await this.apply(this.current());
  }

  async use(language: Language): Promise<void> {
    writeChoice(LANGUAGE_KEY, language);
    await this.apply(language);
  }

  private async apply(language: Language): Promise<void> {
    // The switch waits for the translations, so the page never shows a language half loaded.
    await firstValueFrom(this.transloco.load(language));
    this.transloco.setActiveLang(language);
    this.root.lang = language;
    this.current.set(language);
  }
}
