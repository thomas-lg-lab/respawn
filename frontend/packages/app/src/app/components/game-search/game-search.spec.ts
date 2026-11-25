import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { LibraryGame } from '@respawn/data';
import { type } from '@app/testing/app-testing';
import { game } from '@app/testing/games';
import { testTranslations } from '@app/testing/translations';
import { GameSearch } from './game-search';

const HOUR = 3600;

/** Six titles hold "ha"; one has an accent, one was never launched, one has no known playtime. */
function library(): LibraryGame[] {
  return [
    game('Half-Life', { playtime_seconds: 5 * HOUR }),
    game('Hades', { playtime_seconds: 100 * HOUR }),
    game('Halo Infinite'),
    game('Shadow Tactics', { playtime_seconds: 2 * HOUR }),
    game('Hades II', { playtime_seconds: 40 * HOUR }),
    game('Half-Life 2', { playtime_seconds: 14 * HOUR }),
    game('Pokémon', { playtime_seconds: null, recent_playtime_seconds: null }),
    game('Celeste', { playtime_seconds: 25 * 60 }),
  ];
}

@Component({
  imports: [GameSearch],
  template: `<rs-game-search
    language="fr"
    [games]="games()"
    (searched)="asked.push($event)"
    (opened)="opened.push(true)"
  />`,
})
class Host {
  readonly games = signal<LibraryGame[] | undefined>(library());
  readonly asked: string[] = [];
  readonly opened: boolean[] = [];
}

describe('GameSearch', () => {
  async function create(games: LibraryGame[] | null = library()) {
    TestBed.configureTestingModule({ imports: [testTranslations('fr')] });
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.games.set(games ?? undefined);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const input = element.querySelector('input')!;

    return {
      fixture,
      element,
      input,
      host: fixture.componentInstance,
      /** The focus comes to the field, then this is typed. */
      search: async (typed: string) => {
        input.dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
        type(input, typed);
        await fixture.whenStable();
      },
      press: async (key: string) => {
        input.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
        await fixture.whenStable();
      },
    };
  }

  const said = (within: Element, selector: string) =>
    [...within.querySelectorAll(selector)].map((each) =>
      each.textContent.replace(/\s+/g, ' ').trim(),
    );
  const titles = (element: HTMLElement) => said(element, '[data-suggestion] [data-title]');

  it('suggests nothing under two letters, then the games that hold them, the closest first', async () => {
    const { element, input, search } = await create();

    await search('h');
    expect(element.querySelector('[data-search-panel]')).toBeNull();
    expect(input.getAttribute('aria-expanded')).toBe('false');

    await search('ha');
    // Five at most, the titles that start with it first, the most played of them first: the
    // sixth, which only holds it, is a search of the Library away.
    expect(titles(element)).toEqual([
      'Hades',
      'Hades II',
      'Half-Life 2',
      'Half-Life',
      'Halo Infinite',
    ]);
    expect(said(element, '[data-suggestion] [data-time]')).toEqual([
      '100 h',
      '40 h',
      '14 h',
      '5 h',
      'Jamais lancé',
    ]);
    expect(said(element, '[data-suggestion] mark')).toEqual(['Ha', 'Ha', 'Ha', 'Ha', 'Ha']);
    expect(said(element, '[data-search-all]')).toEqual(['Voir les 6 jeux dans la bibliothèque']);
    expect(input.getAttribute('aria-expanded')).toBe('true');
    expect(element.querySelector('[role="listbox"]')?.id).toBe(input.getAttribute('aria-controls'));
  });

  it('finds a title whatever its accents, and says a playtime no store gave or a game never launched', async () => {
    const { element, search } = await create();

    await search('poke');
    expect(titles(element)).toEqual(['Pokémon']);
    expect(said(element, '[data-suggestion] mark')).toEqual(['Poké']);
    expect(said(element, '[data-suggestion] [data-time]')).toEqual(['—']);
    expect(said(element, '[data-search-all]')).toEqual(['Voir 1 jeu dans la bibliothèque']);

    await search('halo');
    expect(said(element, '[data-suggestion] [data-time]')).toEqual(['Jamais lancé']);

    await search('cel');
    expect(said(element, '[data-suggestion] [data-time]')).toEqual(['25 min']);
  });

  it('asks for what was typed on Enter, even one letter, and is free again', async () => {
    const { element, input, host, search, press } = await create();

    await search(' h ');
    await press('Enter');

    expect(host.asked).toEqual(['h']);
    expect(input.value).toBe('');
    expect(element.querySelector('[data-search-panel]')).toBeNull();

    // Nothing typed: nothing to ask for.
    await press('Enter');
    expect(host.asked).toEqual(['h']);
  });

  it('asks for the title of the suggestion the arrows chose, or the one pressed', async () => {
    const { element, input, host, search, press } = await create();

    await search('ha');
    expect(input.hasAttribute('aria-activedescendant')).toBe(false);
    await press('ArrowDown');
    await press('ArrowDown');
    const chosen = element.querySelector('[data-suggestion][aria-selected="true"]')!;
    expect(chosen.textContent).toContain('Hades II');
    expect(input.getAttribute('aria-activedescendant')).toBe(chosen.id);
    await press('Enter');
    expect(host.asked).toEqual(['Hades II']);

    // From the first, up goes to the last; a letter more and no suggestion is chosen.
    await search('ha');
    await press('ArrowUp');
    expect(element.querySelector('[data-suggestion][aria-selected="true"]')?.textContent).toContain(
      'Halo Infinite',
    );
    await search('had');
    expect(element.querySelector('[data-suggestion][aria-selected="true"]')).toBeNull();

    element.querySelector<HTMLButtonElement>('[data-suggestion]')!.click();
    expect(host.asked).toEqual(['Hades II', 'Hades']);
  });

  it('leads to every game found from the foot of the suggestions', async () => {
    const { element, host, search } = await create();

    await search('half');
    element.querySelector<HTMLButtonElement>('[data-search-all]')!.click();

    expect(host.asked).toEqual(['half']);
  });

  it('says that no game matches what was typed', async () => {
    const { element, search } = await create();

    await search('zelda');

    expect(said(element, '[data-search-none]')).toEqual(['Aucun jeu ne correspond à « zelda ».']);
    expect(element.querySelector('[data-search-all]')).toBeNull();
  });

  it('says its field was used, and suggests nothing while the games are not read', async () => {
    const { element, host, search } = await create(null);

    await search('ha');

    expect(host.opened).toEqual([true]);
    expect(element.querySelector('[data-search-panel]')).toBeNull();
  });

  it('hints at its shortcut while empty, and clears what was typed on request', async () => {
    const { fixture, element, input, search } = await create();
    expect(said(element, 'kbd')).toEqual(['Ctrl K']);

    await search('ha');
    expect(element.querySelector('kbd')).toBeNull();
    element.querySelector<HTMLButtonElement>('[data-search-clear]')!.click();
    await fixture.whenStable();

    expect(input.value).toBe('');
    expect(said(element, 'kbd')).toEqual(['Ctrl K']);
  });

  it('takes the focus on Ctrl+K from anywhere; Escape gives up what was typed, then the focus', async () => {
    const { fixture, element, input, search, press } = await create();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }));
    await fixture.whenStable();
    expect(document.activeElement).toBe(input);

    await search('ha');
    await press('Escape');

    expect(input.value).toBe('');
    expect(element.querySelector('[data-search-panel]')).toBeNull();
    expect(document.activeElement).toBe(input);

    await press('Escape');
    expect(document.activeElement).not.toBe(input);
  });

  it('closes when the focus leaves it, what was typed still there', async () => {
    const { fixture, element, input, search } = await create();

    await search('ha');
    input.dispatchEvent(
      new FocusEvent('focusout', { bubbles: true, relatedTarget: document.body }),
    );
    await fixture.whenStable();

    expect(element.querySelector('[data-search-panel]')).toBeNull();
    expect(input.value).toBe('ha');
  });
});
