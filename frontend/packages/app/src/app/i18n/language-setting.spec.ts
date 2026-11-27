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

/**
 * Stands in for the build's chunks: a language on `gone` cannot be fetched, one on `slow`
 * arrives only when `arrive` is called.
 */
class Chunks implements TranslocoLoader {
  static gone: Language[] = [];
  static slow: Language[] = [];
  static asked: string[] = [];
  static arrive: () => void = () => undefined;

  getTranslation(language: string): Promise<Translation> {
    Chunks.asked.push(language);
    const translation = language === 'fr' ? fr : en;
    if (Chunks.gone.includes(language as Language)) {
      return Promise.reject(new Error(`Failed to fetch the chunk of ${language}`));
    }
    if (Chunks.slow.includes(language as Language)) {
      return new Promise((resolve) => (Chunks.arrive = () => resolve(translation)));
    }
    return Promise.resolve(translation);
  }
}

describe('LanguageSetting', () => {
  const root = document.documentElement;

  function create(gone: Language[] = []) {
    Chunks.gone = gone;
    Chunks.slow = [];
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
    expect(transloco.translate('settings.theme')).toBe('Thème');
  });

  it('still starts, in the fallback language, when the kept one cannot be fetched', async () => {
    localStorage.setItem('respawn.language', 'fr');
    const { setting, transloco } = create(['fr']);

    await setting.start();

    expect(setting.current()).toBe('en');
    expect(root.lang).toBe('en');
    expect(transloco.getActiveLang()).toBe('en');
    expect(transloco.translate('settings.theme')).toBe('Theme');
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

    expect(switched).toBe('unavailable');
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

    expect(switched).toBe('shown');
    expect(setting.current()).toBe('fr');
    expect(transloco.translate('settings.theme')).toBe('Thème');
    expect(localStorage.getItem('respawn.language')).toBe('fr');
  });

  it('shows the language asked for last, whichever file arrives first', async () => {
    localStorage.setItem('respawn.language', 'fr');
    const { setting } = create();
    await setting.start();
    Chunks.slow = ['en'];

    const toEnglish = setting.use('en');
    expect(setting.chosen()).toBe('en');
    const toFrench = await setting.use('fr');
    Chunks.arrive();

    expect(toFrench).toBe('shown');
    expect(await toEnglish).toBe('superseded');
    expect(setting.current()).toBe('fr');
    expect(setting.chosen()).toBe('fr');
    expect(localStorage.getItem('respawn.language')).toBe('fr');
  });
});
