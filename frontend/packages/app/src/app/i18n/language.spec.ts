import { browserLanguage } from './language';
import en from './en.json';
import fr from './fr.json';
import foundationsEn from '../pages/foundations/i18n/en.json';
import foundationsFr from '../pages/foundations/i18n/fr.json';

/** Every key of a translation file, as dotted paths. */
function keys(node: unknown, prefix = ''): string[] {
  if (typeof node !== 'object' || node === null) return [prefix];
  return Object.entries(node).flatMap(([name, value]) =>
    keys(value, prefix ? `${prefix}.${name}` : name),
  );
}

describe('browserLanguage', () => {
  it('takes the first language of the browser that the app is written in', () => {
    expect(browserLanguage(['fr-FR', 'en'])).toBe('fr');
    expect(browserLanguage(['de', 'en-GB', 'fr'])).toBe('en');
    expect(browserLanguage(['FR-ca'])).toBe('fr');
  });

  it('falls back to English for a browser that asks for neither', () => {
    expect(browserLanguage(['de-DE', 'es'])).toBe('en');
    expect(browserLanguage([])).toBe('en');
  });

  it('does not take a language for another that starts the same', () => {
    // Frisian is `fy`, Friulian `fur`: neither is French.
    expect(browserLanguage(['fur', 'fy'])).toBe('en');
  });
});

describe('translations', () => {
  it('have the same keys in both languages', () => {
    expect(keys(fr).sort()).toEqual(keys(en).sort());
    expect(keys(foundationsFr).sort()).toEqual(keys(foundationsEn).sort());
  });

  it('leave no key empty', () => {
    for (const file of [fr, en, foundationsFr, foundationsEn]) {
      const empty = keys(file).filter((path) => {
        const value = path.split('.').reduce<unknown>((node, name) => {
          return (node as Record<string, unknown>)[name];
        }, file);
        return typeof value !== 'string' || value.trim() === '';
      });
      expect(empty).toEqual([]);
    }
  });
});
