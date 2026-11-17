import { TestBed } from '@angular/core/testing';
import {
  type Translation,
  type TranslocoLoader,
  TranslocoService,
  provideTransloco,
} from '@jsverse/transloco';
import en from './en.json';
import fr from './fr.json';
import { type Language, LanguageSetting, initialLanguage } from './language';

/** Stands in for the build's chunks: a language on `gone` cannot be fetched. */
class Chunks implements TranslocoLoader {
  static gone: Language[] = [];
  static asked: string[] = [];

  getTranslation(language: string): Promise<Translation> {
    Chunks.asked.push(language);
    return Chunks.gone.includes(language as Language)
      ? Promise.reject(new Error(`Failed to fetch the chunk of ${language}`))
      : Promise.resolve(language === 'fr' ? fr : en);
  }
}

describe('LanguageSetting', () => {
  const root = document.documentElement;

  function create(gone: Language[] = []) {
    Chunks.gone = gone;
    Chunks.asked = [];
    TestBed.configureTestingModule({
      providers: [
        provideTransloco({
          config: {
            availableLangs: ['fr', 'en'],
            defaultLang: initialLanguage(),
            fallbackLang: 'en',
            reRenderOnLangChange: true,
          },
          loader: Chunks,
        }),
      ],
    });
    return {
      setting: TestBed.inject(LanguageSetting),
      transloco: TestBed.inject(TranslocoService),
    };
  }

  beforeEach(() => {
    localStorage.clear();
    root.lang = '';
  });

  it('starts in the language kept in the browser', async () => {
    localStorage.setItem('respawn.language', 'fr');
    const { setting, transloco } = create();

    await setting.start();

    expect(setting.current()).toBe('fr');
    expect(root.lang).toBe('fr');
    expect(transloco.translate('foundations.label')).toBe('Fondations');
  });

  it('still starts, in the fallback language, when the kept one cannot be fetched', async () => {
    localStorage.setItem('respawn.language', 'fr');
    const { setting, transloco } = create(['fr']);

    await setting.start();

    expect(setting.current()).toBe('en');
    expect(root.lang).toBe('en');
    expect(transloco.getActiveLang()).toBe('en');
    expect(transloco.translate('foundations.label')).toBe('Foundations');
    // The choice was not at fault: it is still there for the next visit.
    expect(localStorage.getItem('respawn.language')).toBe('fr');
  });

  it('still starts when no language at all can be fetched', async () => {
    const { setting } = create(['fr', 'en']);

    await expect(setting.start()).resolves.toBeUndefined();
  });

  it('remembers a chosen language only once it has loaded', async () => {
    localStorage.setItem('respawn.language', 'en');
    const { setting, transloco } = create(['fr']);
    await setting.start();

    const switched: unknown = await setting.use('fr');

    expect(switched).toBe(false);
    expect(setting.current()).toBe('en');
    expect(root.lang).toBe('en');
    expect(transloco.getActiveLang()).toBe('en');
    expect(localStorage.getItem('respawn.language')).toBe('en');
  });

  it('can be chosen again once its chunk is back', async () => {
    localStorage.setItem('respawn.language', 'en');
    const { setting, transloco } = create(['fr']);
    await setting.start();
    await setting.use('fr');

    Chunks.gone = [];
    const switched: unknown = await setting.use('fr');

    expect(switched).toBe(true);
    expect(setting.current()).toBe('fr');
    expect(transloco.translate('foundations.label')).toBe('Fondations');
    expect(localStorage.getItem('respawn.language')).toBe('fr');
  });
});
