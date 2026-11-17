import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import en from '@app/i18n/en.json';
import fr from '@app/i18n/fr.json';
import { Foundations } from './foundations';

describe('Foundations', () => {
  const root = document.documentElement;

  async function create(language: 'fr' | 'en') {
    localStorage.setItem('respawn.language', language);
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { fr, en },
          translocoConfig: {
            availableLangs: ['fr', 'en'],
            defaultLang: language,
            reRenderOnLangChange: true,
          },
          preloadLangs: true,
        }),
      ],
    });
    const fixture = TestBed.createComponent(Foundations);
    await fixture.whenStable();
    return fixture;
  }

  const element = (fixture: Awaited<ReturnType<typeof create>>) =>
    fixture.nativeElement as HTMLElement;

  const pill = (fixture: Awaited<ReturnType<typeof create>>, selector: string) =>
    element(fixture).querySelector<HTMLButtonElement>(selector)!;

  beforeEach(() => {
    localStorage.clear();
    root.removeAttribute('data-theme');
    root.removeAttribute('data-character');
  });

  it('is written in the language in use, numbers included', async () => {
    const french = await create('fr');
    expect(element(french).querySelector('h1')?.textContent).toBe('Les fondations visuelles');
    // French groups thousands with a narrow no-break space.
    expect(element(french).querySelector('.type-figure')?.textContent).toMatch(/^1\s276$/);

    TestBed.resetTestingModule();
    const english = await create('en');
    expect(element(english).querySelector('h1')?.textContent).toBe('The visual foundations');
    expect(element(english).querySelector('.type-figure')?.textContent).toBe('1,276');
  });

  it('switches to the other language without a reload', async () => {
    const fixture = await create('fr');

    pill(fixture, '[data-language-choice="en"]').click();

    // The switch waits for the translations of the new language before it shows.
    await vi.waitFor(() =>
      expect(element(fixture).querySelector('h1')?.textContent).toBe('The visual foundations'),
    );
    expect(pill(fixture, '[data-language-choice="en"]').getAttribute('aria-pressed')).toBe('true');
    expect(root.lang).toBe('en');
    expect(localStorage.getItem('respawn.language')).toBe('en');
  });

  it('starts on the system theme and puts a chosen one on the root element', async () => {
    const fixture = await create('en');
    expect(pill(fixture, '[data-theme-choice="system"]').getAttribute('aria-pressed')).toBe('true');
    expect(root.hasAttribute('data-theme')).toBe(false);

    pill(fixture, '[data-theme-choice="light"]').click();
    await fixture.whenStable();

    expect(root.getAttribute('data-theme')).toBe('light');
    expect(pill(fixture, '[data-theme-choice="light"]').getAttribute('aria-pressed')).toBe('true');
    expect(pill(fixture, '[data-theme-choice="system"]').getAttribute('aria-pressed')).toBe(
      'false',
    );
  });

  it('switches the character on the root element', async () => {
    const fixture = await create('en');

    pill(fixture, '[data-character-choice="plain"]').click();
    await fixture.whenStable();

    expect(root.getAttribute('data-character')).toBe('plain');
  });

  it('names the button that shows only an icon', async () => {
    const fixture = await create('fr');

    expect(element(fixture).querySelector('button[aria-label="Synchroniser"]')).not.toBe(null);
  });
});
