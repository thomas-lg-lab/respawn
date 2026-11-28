import { Location } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { type LibraryGame, PROVIDER_OUTCOME, type StoreTag } from '@respawn/data';
import { TEST_LINKED_ACCOUNT, TEST_SYNC_RUN } from '@respawn/data/testing';
import { QueryClient } from '@tanstack/angular-query';
import {
  LIBRARY_FIRST_PAGE,
  api,
  button,
  openWithGames,
  text,
  type,
} from '@app/testing/app-testing';
import type { Language } from '@app/i18n/language';
import '../../layout/shell';
import './library';
import { ago, game } from '@app/testing/games';

/** A small library, as the API gives it: by title. */
function library(): LibraryGame[] {
  return [
    game('Celeste', { genres: ['Platform', 'Indie'] }),
    game('Hades', {
      playtime_seconds: 360000,
      last_played_at: ago(400),
      genres: ['Role-playing (RPG)', 'Indie'],
      themes: ['Action'],
    }),
    game('Pokémon', { playtime_seconds: null, recent_playtime_seconds: null }),
    game('Portal 2', {
      playtime_seconds: 7200,
      recent_playtime_seconds: 2700,
      last_played_at: ago(1),
    }),
  ];
}

describe('Library', () => {
  /** Opens the library, signed in; the API answers with these games unless told not to. */
  const open = (
    games: LibraryGame[] | null = library(),
    language: Language = 'fr',
    address = '/library',
  ) => openWithGames(address, games, language);

  const names = (screen: HTMLElement) =>
    [...screen.querySelectorAll('[data-name]')].map((each) => each.textContent);
  /** A game's row of the list or its tile among the covers. */
  const row = (screen: HTMLElement, name: string) =>
    [...screen.querySelectorAll('[data-game]')].find((each) => text(each, '[data-name]') === name)!;
  /** The list, rather than the covers, as this browser would remember it. */
  const asList = () => localStorage.setItem('respawn.library-view', 'list');
  const viewPill = (screen: HTMLElement, view: string) =>
    screen.querySelector<HTMLButtonElement>(`[data-view="${view}"]`)!;
  const pill = (screen: HTMLElement, filter: string) =>
    screen.querySelector<HTMLButtonElement>(`[data-filter="${filter}"]`)!;

  beforeEach(() => localStorage.clear());
  afterEach(() => api().http.verify());

  it('lists the games, the latest session first, and says how many there are', async () => {
    const { screen } = await open();

    expect(screen.querySelector('h1')?.textContent).toBe('Bibliothèque');
    expect(text(screen, '[data-owned]')).toBe('4 jeux, depuis Steam');
    expect(names(screen)).toEqual(['Portal 2', 'Hades', 'Celeste', 'Pokémon']);
    expect(text(screen, '[data-shown]')).toBe('4 jeux affichés sur 4');
    expect(screen.querySelector('[data-more]')).toBeNull();
  });

  it('shows the games as covers first, each with its playtime, its cover asked of the API', async () => {
    const { screen } = await open();

    expect(screen.querySelector('rs-game-grid')).not.toBeNull();
    expect(screen.querySelector('rs-game-table')).toBeNull();
    expect(viewPill(screen, 'covers').getAttribute('aria-pressed')).toBe('true');
    const portal = row(screen, 'Portal 2');
    expect(text(portal, '[data-playtime]')).toBe('2 h');
    expect(portal.querySelector('img')?.getAttribute('src')).toBe(
      `/api/v1/games/${portal.getAttribute('data-game')}/cover`,
    );
    expect(text(row(screen, 'Celeste'), '[data-playtime]')).toBe('Jamais lancé');
    // Nothing said under a title when the store did not say.
    expect(row(screen, 'Pokémon').querySelector('[data-playtime]')).toBeNull();
  });

  it('shows the cover the account chose for a game, by its own address', async () => {
    const games = library();
    games[0] = { ...games[0]!, cover_source: 'igdb' };
    const { screen } = await open(games);

    const celeste = row(screen, 'Celeste');
    expect(celeste.querySelector('img')?.getAttribute('src')).toBe(
      `/api/v1/games/${celeste.getAttribute('data-game')}/cover?source=igdb`,
    );
  });

  it('shows the same games as a list on request, and this browser remembers it', async () => {
    const { harness, screen } = await open();

    viewPill(screen, 'list').click();
    await harness.fixture.whenStable();

    expect(screen.querySelector('rs-game-table')).not.toBeNull();
    expect(screen.querySelector('rs-game-grid')).toBeNull();
    expect(viewPill(screen, 'list').getAttribute('aria-pressed')).toBe('true');
    expect(names(screen)).toEqual(['Portal 2', 'Hades', 'Celeste', 'Pokémon']);
    expect(localStorage.getItem('respawn.library-view')).toBe('list');
  });

  it('shows the covers again when what this browser kept is no view it knows', async () => {
    localStorage.setItem('respawn.library-view', 'mosaic');
    const { screen } = await open();

    expect(screen.querySelector('rs-game-grid')).not.toBeNull();
  });

  it('reads the games again once a sync ended, learnt on this screen', async () => {
    const { harness, server, screen } = await open();

    // The window is back in front, say: the accounts are asked again, and a sync ended since.
    void TestBed.inject(QueryClient).invalidateQueries({ queryKey: ['linked-accounts'] });
    const last = { ...TEST_SYNC_RUN, id: '6a1d2e3f-4b5c-4d6e-8f70-1a2b3c4d5e6f' };
    await server.answer('/linked-accounts', {
      accounts: [{ ...TEST_LINKED_ACCOUNT, sync: { ...TEST_LINKED_ACCOUNT.sync, last } }],
    });
    const games = [...library(), game('Outer Wilds')];
    await server.answer(LIBRARY_FIRST_PAGE, { total: games.length, offset: 0, limit: 500, games });
    await harness.fixture.whenStable();

    expect(names(screen)).toContain('Outer Wilds');
  });

  it('says a playtime, the last two weeks and the last session of a game', async () => {
    asList();
    const { screen } = await open();
    const portal = row(screen, 'Portal 2');

    expect(text(portal, '[data-playtime]')).toBe('2 h');
    expect(text(portal, '[data-recent]')).toBe('45 min');
    expect(text(portal, '[data-last-played]')).toBe('Hier');
    expect(text(row(screen, 'Hades'), '[data-playtime]')).toBe('100 h');
    expect(text(row(screen, 'Hades'), '[data-recent]')).toBe('');
    expect(text(row(screen, 'Hades'), '[data-last-played]')).toBe('Il y a 1 an');
  });

  it('names the table, and says the last session under the title too, for a phone', async () => {
    asList();
    const { screen } = await open();

    expect(text(screen, 'caption')).toBe('Bibliothèque');
    expect(text(row(screen, 'Portal 2'), '[data-last-played-under]')).toBe('Hier');
    expect(text(row(screen, 'Celeste'), '[data-last-played-under]')).toBe('Jamais lancé');
    // Nothing to say under a title when the store did not say.
    expect(row(screen, 'Pokémon').querySelector('[data-last-played-under]')).toBeNull();
  });

  it('tells a game never launched from one the store says nothing about', async () => {
    asList();
    const { screen } = await open();

    expect(text(row(screen, 'Celeste'), '[data-playtime]')).toBe('0 min');
    expect(text(row(screen, 'Celeste'), '[data-last-played]')).toBe('Jamais lancé');
    expect(text(row(screen, 'Pokémon'), '[data-playtime]')).toBe('—');
    expect(text(row(screen, 'Pokémon'), '[data-recent]')).toBe('—');
    expect(text(row(screen, 'Pokémon'), '[data-last-played]')).toBe('—');
  });

  it('keeps the title its address asks for, and counts what each filter would keep of it', async () => {
    const { screen } = await open(library(), 'fr', '/library?q=po');

    expect(text(screen, '[data-searched]')).toBe('Recherche : « po »');
    expect(names(screen)).toEqual(['Portal 2', 'Pokémon']);
    expect(text(screen, '[data-filter="all"]')).toBe('Tous 2');
    expect(text(screen, '[data-filter="launched"]')).toBe('Lancés 1');
    expect(text(screen, '[data-filter="never"]')).toBe('Jamais lancés 1');
  });

  it('filters, and says which filter is on', async () => {
    const { harness, screen } = await open();

    pill(screen, 'never').click();
    await harness.fixture.whenStable();

    expect(names(screen)).toEqual(['Celeste', 'Pokémon']);
    expect(pill(screen, 'never').getAttribute('aria-pressed')).toBe('true');
    expect(pill(screen, 'all').getAttribute('aria-pressed')).toBe('false');
  });

  it('goes back to every game when another title is searched, whatever filter was on', async () => {
    const { harness, screen } = await open();
    pill(screen, 'never').click();
    await harness.fixture.whenStable();

    // Asked from the header: a game that was played, which "never launched" would hide.
    await harness.navigateByUrl('/library?q=hades');
    await harness.fixture.whenStable();

    expect(names(screen)).toEqual(['Hades']);
    expect(pill(screen, 'all').getAttribute('aria-pressed')).toBe('true');

    // Clearing the search asks for nothing new: a filter picked meanwhile stays.
    pill(screen, 'launched').click();
    await harness.fixture.whenStable();
    button(screen, 'Effacer la recherche').click();
    await harness.fixture.whenStable();

    expect(pill(screen, 'launched').getAttribute('aria-pressed')).toBe('true');
    expect(names(screen)).toEqual(['Portal 2', 'Hades']);
  });

  it('shows every game that bears a title searched again, a filter left on since', async () => {
    const { harness, screen } = await open(library(), 'fr', '/library?q=hades');
    pill(screen, 'never').click();
    await harness.fixture.whenStable();
    expect(names(screen)).toEqual([]);

    // The same title, asked again in the header: the address does not change.
    const search = screen.querySelector<HTMLInputElement>('[data-search] input')!;
    type(search, 'hades');
    await harness.fixture.whenStable();
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await harness.fixture.whenStable();

    expect(names(screen)).toEqual(['Hades']);
    expect(pill(screen, 'all').getAttribute('aria-pressed')).toBe('true');
  });

  it('keeps the filter when the history leads back to a title: nothing was searched', async () => {
    const { harness, screen } = await open(library(), 'fr', '/library?q=po');
    // The router follows the browser's history as it does once the app has started.
    TestBed.inject(Router).setUpLocationChangeListener();
    await harness.navigateByUrl('/library?q=hades');
    await harness.fixture.whenStable();
    pill(screen, 'launched').click();
    await harness.fixture.whenStable();

    const location = TestBed.inject(Location);
    location.back();
    await vi.waitFor(() => expect(text(screen, '[data-searched]')).toBe('Recherche : « po »'));
    location.forward();
    await vi.waitFor(() => expect(text(screen, '[data-searched]')).toBe('Recherche : « hades »'));

    expect(pill(screen, 'launched').getAttribute('aria-pressed')).toBe('true');
  });

  it('takes a title made of spaces or of signs for no search', async () => {
    const { harness, screen } = await open(library(), 'fr', '/library?q=%20');
    expect(screen.querySelector('[data-searched]')).toBeNull();
    expect(names(screen)).toHaveLength(4);

    await harness.navigateByUrl('/library?q=%5E%5E');
    await harness.fixture.whenStable();
    expect(screen.querySelector('[data-searched]')).toBeNull();
    expect(names(screen)).toHaveLength(4);
  });

  it('does not say again the word of a link when the search is cleared', async () => {
    const { harness, screen } = await open(
      library(),
      'fr',
      `/library?q=po&${PROVIDER_OUTCOME}=linked`,
    );
    // Said once: the word left the address the browser shows, the title stayed.
    expect(TestBed.inject(Location).path()).toBe('/library?q=po');

    button(screen, 'Effacer la recherche').click();
    await harness.fixture.whenStable();

    expect(TestBed.inject(Location).path()).toBe('/library');
    expect(screen.querySelector('rs-banner')).toBeNull();
  });

  it('opens on the filter and the order its address asks for, as the home links to them', async () => {
    const { screen } = await open(library(), 'fr', '/library?filter=launched&sort=playtime');

    expect(names(screen)).toEqual(['Hades', 'Portal 2']);
    expect(pill(screen, 'launched').getAttribute('aria-pressed')).toBe('true');
    expect(screen.querySelector<HTMLSelectElement>('[data-sort]')!.value).toBe('playtime');
  });

  it('takes a filter or an order it does not know for the first one', async () => {
    const { screen } = await open(library(), 'fr', '/library?filter=played&sort=size');

    expect(names(screen)).toEqual(['Portal 2', 'Hades', 'Celeste', 'Pokémon']);
    expect(pill(screen, 'all').getAttribute('aria-pressed')).toBe('true');
  });

  it('writes the filter and the order picked on its address, in place', async () => {
    const { harness, screen } = await open(library(), 'fr', '/library?q=po');
    const location = TestBed.inject(Location);
    const going = vi.spyOn(TestBed.inject(Router), 'navigateByUrl');

    pill(screen, 'never').click();
    await harness.fixture.whenStable();
    const sort = screen.querySelector<HTMLSelectElement>('[data-sort]')!;
    sort.value = 'name';
    sort.dispatchEvent(new Event('change'));
    await harness.fixture.whenStable();
    expect(location.path()).toBe('/library?q=po&filter=never&sort=name');

    // The first ones are not written.
    pill(screen, 'all').click();
    await harness.fixture.whenStable();
    expect(location.path()).toBe('/library?q=po&sort=name');
    expect(names(screen)).toEqual(['Pokémon', 'Portal 2']);
    // In place: the browser's history gains no step.
    expect(going.mock.calls.every(([, extras]) => extras?.replaceUrl)).toBe(true);
    expect(going).toHaveBeenCalledTimes(3);
  });

  it('sorts by playtime or by title', async () => {
    const { harness, screen } = await open();
    const sort = screen.querySelector<HTMLSelectElement>('[data-sort]')!;
    expect(sort.value).toBe('last_played');

    sort.value = 'playtime';
    sort.dispatchEvent(new Event('change'));
    await harness.fixture.whenStable();
    expect(names(screen)).toEqual(['Hades', 'Portal 2', 'Celeste', 'Pokémon']);

    sort.value = 'name';
    sort.dispatchEvent(new Event('change'));
    await harness.fixture.whenStable();
    expect(names(screen)).toEqual(['Celeste', 'Hades', 'Pokémon', 'Portal 2']);
  });

  it('keeps the genre or theme its address asks for, a game’s page leading there', async () => {
    const { harness, screen } = await open(library(), 'fr', '/library?genre=Indie&sort=name');

    expect(text(screen, '[data-chip="tag:Indie"]')).toBe('Indie');
    expect(names(screen)).toEqual(['Celeste', 'Hades']);
    expect(text(screen, '[data-filter="all"]')).toBe('Tous 2');
    expect(text(screen, '[data-filter="never"]')).toBe('Jamais lancés 1');

    // A theme narrows it the same way.
    await harness.navigateByUrl('/library?genre=Action');
    expect(names(screen)).toEqual(['Hades']);

    const chip = screen.querySelector<HTMLButtonElement>('[data-chip="tag:Action"]')!;
    expect(chip.getAttribute('aria-label')).toBe('Retirer le filtre Action');
    chip.click();
    await harness.fixture.whenStable();

    expect(names(screen)).toHaveLength(4);
    expect(screen.querySelector('[data-chip]')).toBeNull();
    expect(TestBed.inject(Location).path()).toBe('/library');
  });

  describe('the menus of genres, themes and lengths', () => {
    const hours = 3600;
    /** A library with durations: a game in each length, and one IGDB says none of. */
    const timed = (): LibraryGame[] => [
      game('Celeste', { genres: ['Platform', 'Indie'], normally_seconds: 8 * hours }),
      game('Disco Elysium', { genres: ['Role-playing (RPG)'], normally_seconds: 32 * hours }),
      game('Hades', {
        playtime_seconds: 360000,
        last_played_at: ago(400),
        genres: ['Role-playing (RPG)', 'Indie'],
        themes: ['Action'],
        normally_seconds: 22 * hours,
      }),
      game('Pokémon', { normally_seconds: null }),
    ];
    const menu = (screen: HTMLElement, kind: string) =>
      screen.querySelector<HTMLButtonElement>(`[data-filter-menu="${kind}"]`)!;
    const panel = (screen: HTMLElement, kind: string) =>
      screen.querySelector<HTMLElement>(`[data-filter-panel="${kind}"]`)!;
    const choices = (screen: HTMLElement, kind: string) =>
      [...panel(screen, kind).querySelectorAll('[data-choice]')].map((each) =>
        [...each.querySelectorAll('span')].map((part) => part.textContent?.trim()).join(' '),
      );
    const tick = async (
      harness: { fixture: { whenStable(): Promise<unknown> } },
      screen: HTMLElement,
      kind: string,
      id: string,
    ) => {
      panel(screen, kind).querySelector<HTMLInputElement>(`[data-choice="${id}"] input`)!.click();
      await harness.fixture.whenStable();
    };

    it('lists the genres the games bear, the most borne first, and keeps the games that bear all those ticked', async () => {
      const { harness, screen } = await open(timed(), 'fr', '/library?sort=name');

      expect(panel(screen, 'genres').hidden).toBe(true);
      menu(screen, 'genres').click();
      await harness.fixture.whenStable();
      expect(panel(screen, 'genres').hidden).toBe(false);
      expect(choices(screen, 'genres')).toEqual(['Indie 2', 'Role-playing (RPG) 2', 'Platform 1']);

      await tick(harness, screen, 'genres', 'Indie');
      expect(names(screen)).toEqual(['Celeste', 'Hades']);
      expect(TestBed.inject(Location).path()).toBe('/library?sort=name&genre=Indie');

      // A second genre narrows the list further: the games that have both.
      await tick(harness, screen, 'genres', 'Role-playing (RPG)');
      expect(names(screen)).toEqual(['Hades']);
      expect(text(screen, '[data-chip="tag:Role-playing (RPG)"]')).toBe('RPG');
      expect(text(menu(screen, 'genres'), '[data-picked]')).toBe('2');
      // The ticked ones come first; a genre no game left bears is no longer offered.
      expect(choices(screen, 'genres')).toEqual(['Indie 1', 'Role-playing (RPG) 1']);
      expect(text(screen, '[data-filter="all"]')).toBe('Tous 1');
    });

    it('finds a genre by a part of its name', async () => {
      const { harness, screen } = await open(timed(), 'fr', '/library?sort=name');
      menu(screen, 'genres').click();
      await harness.fixture.whenStable();

      const field = panel(screen, 'genres').querySelector<HTMLInputElement>('[data-filter-find]')!;
      field.value = 'role';
      field.dispatchEvent(new Event('input'));
      await harness.fixture.whenStable();
      expect(choices(screen, 'genres')).toEqual(['Role-playing (RPG) 2']);

      field.value = 'zzz';
      field.dispatchEvent(new Event('input'));
      await harness.fixture.whenStable();
      expect(text(panel(screen, 'genres'), '[data-no-choice]')).toBe(
        'Aucun genre de ce nom parmi ces jeux.',
      );
    });

    it('says that no game bears a theme, with nothing typed', async () => {
      const { harness, screen } = await open(timed(), 'fr', '/library?sort=name&genre=Platform');
      menu(screen, 'themes').click();
      await harness.fixture.whenStable();

      expect(text(panel(screen, 'themes'), '[data-no-choice]')).toBe('Aucun thème parmi ces jeux.');
    });

    it('keeps the games of any length ticked, neighbours shown as one chip', async () => {
      const { harness, screen } = await open(timed(), 'fr', '/library?sort=name');
      menu(screen, 'lengths').click();
      await harness.fixture.whenStable();
      expect(choices(screen, 'lengths')).toEqual([
        'Moins de 10 h 1',
        '10 à 30 h 1',
        'Plus de 30 h 1',
        'Durée inconnue 1',
      ]);
      expect(panel(screen, 'lengths').querySelector('[data-filter-find]')).toBeNull();

      await tick(harness, screen, 'lengths', 'medium');
      await tick(harness, screen, 'lengths', 'short');
      expect(names(screen)).toEqual(['Celeste', 'Hades']);
      // In the lengths' own order, whichever was ticked first.
      expect(TestBed.inject(Location).path()).toBe('/library?sort=name&length=short&length=medium');
      expect(text(screen, '[data-chip="length:short,medium"]')).toBe('Moins de 30 h');
      // A game has one length: the others keep their counts.
      expect(choices(screen, 'lengths')[2]).toBe('Plus de 30 h 1');

      await tick(harness, screen, 'lengths', 'unknown');
      expect(names(screen)).toEqual(['Celeste', 'Hades', 'Pokémon']);
      expect(text(screen, '[data-chip="length:unknown"]')).toBe('Durée inconnue');

      const span = screen.querySelector<HTMLButtonElement>('[data-chip="length:short,medium"]')!;
      expect(span.getAttribute('aria-label')).toBe('Retirer le filtre Moins de 30 h');
      span.click();
      await harness.fixture.whenStable();
      expect(names(screen)).toEqual(['Pokémon']);
      expect(TestBed.inject(Location).path()).toBe('/library?sort=name&length=unknown');
    });

    it('lists the stores’ tags in the reader’s language and keeps the games that bear all those ticked', async () => {
      const tagged = [
        game('Hollow Knight', { store_tags: ['steam:1628', 'steam:29482'] }),
        game('Sekiro', { store_tags: ['steam:29482', 'steam:4026'] }),
        game('Portal 2', { store_tags: ['steam:1664'] }),
      ];
      const tagNames: StoreTag[] = [
        { key: 'steam:29482', names: { en: 'Souls-like', fr: 'De type Souls' } },
        { key: 'steam:1628', names: { en: 'Metroidvania', fr: 'Metroidvania' } },
        { key: 'steam:1664', names: { en: 'Puzzle' } },
      ];
      const { harness, screen } = await openWithGames('/library?sort=name', tagged, 'fr', tagNames);

      menu(screen, 'tags').click();
      await harness.fixture.whenStable();
      // In French where the store gave it, in English otherwise, by its key when it gave none.
      expect(choices(screen, 'tags')).toEqual([
        'De type Souls 2',
        'Metroidvania 1',
        'Puzzle 1',
        'steam:4026 1',
      ]);

      await tick(harness, screen, 'tags', 'steam:29482');
      expect(names(screen)).toEqual(['Hollow Knight', 'Sekiro']);
      expect(TestBed.inject(Location).path()).toBe('/library?sort=name&tag=steam:29482');
      expect(text(screen, '[data-chip="store-tag:steam:29482"]')).toBe('De type Souls');

      // A second one narrows the list further: the games that have both.
      await tick(harness, screen, 'tags', 'steam:1628');
      expect(names(screen)).toEqual(['Hollow Knight']);
      expect(text(menu(screen, 'tags'), '[data-picked]')).toBe('2');

      const chip = screen.querySelector<HTMLButtonElement>('[data-chip="store-tag:steam:29482"]')!;
      expect(chip.getAttribute('aria-label')).toBe('Retirer le filtre De type Souls');
      chip.click();
      await harness.fixture.whenStable();
      expect(names(screen)).toEqual(['Hollow Knight']);
      expect(TestBed.inject(Location).path()).toBe('/library?sort=name&tag=steam:1628');

      panel(screen, 'tags').querySelector<HTMLButtonElement>('[data-filter-clear]')!.click();
      await harness.fixture.whenStable();
      expect(names(screen)).toEqual(['Hollow Knight', 'Portal 2', 'Sekiro']);
      expect(TestBed.inject(Location).path()).toBe('/library?sort=name');
    });

    it('finds a store’s tag by a part of its name', async () => {
      const tagged = [game('Sekiro', { store_tags: ['steam:29482', 'steam:4026'] })];
      const { harness, screen } = await openWithGames('/library?sort=name', tagged, 'fr', [
        { key: 'steam:29482', names: { fr: 'De type Souls' } },
        { key: 'steam:4026', names: { fr: 'Impitoyable' } },
      ]);
      menu(screen, 'tags').click();
      await harness.fixture.whenStable();

      const field = panel(screen, 'tags').querySelector<HTMLInputElement>('[data-filter-find]')!;
      expect(field.getAttribute('placeholder')).toBe('Chercher un tag');
      field.value = 'souls';
      field.dispatchEvent(new Event('input'));
      await harness.fixture.whenStable();
      expect(choices(screen, 'tags')).toEqual(['De type Souls 1']);

      field.value = 'zzz';
      field.dispatchEvent(new Event('input'));
      await harness.fixture.whenStable();
      expect(text(panel(screen, 'tags'), '[data-no-choice]')).toBe(
        'Aucun tag de ce nom parmi ces jeux.',
      );
    });

    it('clears the ticks of one menu, and leaves the others', async () => {
      const { harness, screen } = await open(
        timed(),
        'fr',
        '/library?sort=name&genre=Indie&genre=Action&length=long',
      );
      expect(names(screen)).toEqual([]);

      menu(screen, 'lengths').click();
      await harness.fixture.whenStable();
      panel(screen, 'lengths').querySelector<HTMLButtonElement>('[data-filter-clear]')!.click();
      await harness.fixture.whenStable();
      expect(names(screen)).toEqual(['Hades']);

      menu(screen, 'themes').click();
      await harness.fixture.whenStable();
      panel(screen, 'themes').querySelector<HTMLButtonElement>('[data-filter-clear]')!.click();
      await harness.fixture.whenStable();
      expect(names(screen)).toEqual(['Celeste', 'Hades']);
      expect(TestBed.inject(Location).path()).toBe('/library?sort=name&genre=Indie');
    });
  });

  it('says that no game matches, and lists every game again once the search is cleared', async () => {
    const { harness, screen } = await open(library(), 'fr', '/library?q=zelda&sort=name');

    expect(text(screen, '[data-no-match]')).toBe('Aucun jeu ne correspond.');
    expect(screen.querySelector('table')).toBeNull();

    button(screen, 'Effacer la recherche').click();
    await harness.fixture.whenStable();

    expect(names(screen)).toHaveLength(4);
    expect(screen.querySelector('[data-searched]')).toBeNull();
    // Only the title left the address.
    expect(TestBed.inject(Location).path()).toBe('/library?sort=name');
  });

  it('shows a long list a step at a time, and starts over when the list changes', async () => {
    const many = Array.from({ length: 230 }, (_, index) =>
      game(`Game ${String(index).padStart(3, '0')}`),
    );
    const { harness, screen } = await open(many);
    expect(names(screen)).toHaveLength(100);
    expect(text(screen, '[data-shown]')).toBe('100 jeux affichés sur 230');

    button(screen, 'Afficher la suite').click();
    await harness.fixture.whenStable();
    expect(names(screen)).toHaveLength(200);

    button(screen, 'Afficher la suite').click();
    await harness.fixture.whenStable();
    expect(names(screen)).toHaveLength(230);
    expect(screen.querySelector('[data-more]')).toBeNull();

    // Another title is searched from the header: the address changes, the screen stays.
    await harness.navigateByUrl('/library?q=game');
    await harness.fixture.whenStable();
    expect(names(screen)).toHaveLength(100);

    // The same title, asked again in the header: the address does not change, the list does.
    button(screen, 'Afficher la suite').click();
    await harness.fixture.whenStable();
    expect(names(screen)).toHaveLength(200);
    const search = screen.querySelector<HTMLInputElement>('[data-search] input')!;
    type(search, 'game');
    await harness.fixture.whenStable();
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await harness.fixture.whenStable();
    expect(names(screen)).toHaveLength(100);
  });

  it('shows as many games again when the history leads back to the list', async () => {
    const many = Array.from({ length: 230 }, (_, index) =>
      game(`Game ${String(index).padStart(3, '0')}`),
    );
    const { harness, screen } = await open(many, 'fr', '/library?sort=name');
    TestBed.inject(Router).setUpLocationChangeListener();
    button(screen, 'Afficher la suite').click();
    await harness.fixture.whenStable();
    button(screen, 'Afficher la suite').click();
    await harness.fixture.whenStable();
    expect(names(screen)).toHaveLength(230);
    // Nothing moved: the address is the same, and the history gained no step.
    const location = TestBed.inject(Location);
    expect(location.path()).toBe('/library?sort=name');

    // Another screen, then the browser's way back: the list is as long as it was left.
    await harness.navigateByUrl('/time');
    await harness.fixture.whenStable();
    expect(screen.querySelector('rs-library')).toBeNull();
    location.back();
    await vi.waitFor(() => expect(names(screen)).toHaveLength(230));

    // Another order is another list: its first games, and the way back to it keeps no more.
    screen.querySelector<HTMLButtonElement>('[data-view="list"]')!.click();
    await harness.fixture.whenStable();
    pill(screen, 'launched').click();
    await harness.fixture.whenStable();
    pill(screen, 'all').click();
    await harness.fixture.whenStable();
    expect(names(screen)).toHaveLength(100);
    await harness.navigateByUrl('/time');
    await harness.fixture.whenStable();
    location.back();
    await vi.waitFor(() => expect(names(screen)).toHaveLength(100));
  });

  it('flags a game no store lists any more', async () => {
    const gone = game('Gone Home', {
      entries: [
        {
          store_entry_id: '00000000-0000-4000-9000-000000000001',
          linked_account_id: '9d2b7a44-1c53-4b7e-9c0a-6e1f2a3b4c5d',
          source: 'steam',
          external_id: '232430',
          playtime_seconds: 0,
          recent_playtime_seconds: 0,
          last_played_at: null,
          first_seen_at: '2026-11-18T10:05:00Z',
          as_of: '2026-11-20T04:02:05Z',
          missing_since: '2026-11-20T04:02:05Z',
        },
      ],
    });
    const { screen } = await open([game('Celeste'), gone]);

    expect(text(row(screen, 'Gone Home'), '[data-missing]')).toBe('N’est plus listé par Steam');
    expect(row(screen, 'Celeste').querySelector('[data-missing]')).toBeNull();
  });

  it('says of a game no source has named that it has no name', async () => {
    const { screen } = await open([game('Celeste'), game('?', { name: null })]);

    expect(names(screen)).toEqual(['Celeste', 'Jeu sans nom']);
  });

  it('leads back to the home while no game was read', async () => {
    const { screen } = await open([]);

    expect(text(screen, '[data-empty]')).toContain('Aucun jeu ici pour l’instant.');
    expect(screen.querySelector('main a[rsButton]')?.getAttribute('href')).toBe('/');
    expect(screen.querySelector('[data-owned]')).toBeNull();
    expect(screen.querySelector('rs-library-toolbar')).toBeNull();
  });

  it('says that the games are on their way', async () => {
    const { harness, server, screen } = await open(null);

    await vi.waitFor(() =>
      expect(text(screen, 'main [role="status"]')).toBe('Chargement de vos jeux…'),
    );
    // The covers' grid, whole rows of blocks where the tiles will stand.
    expect(screen.querySelectorAll('rs-game-grid rs-skeleton')).toHaveLength(12);

    await server.answer(LIBRARY_FIRST_PAGE, { total: 0, offset: 0, limit: 500, games: [] });
    await harness.fixture.whenStable();
  });

  it('says why the games could not be read, and asks again on request', async () => {
    const { harness, server, screen } = await open(null);
    await server.refuse(LIBRARY_FIRST_PAGE, 503, 'database_unavailable');
    await harness.fixture.whenStable();

    expect(text(screen, '[role="alert"]')).toBe(
      'La base de données ne répond pas. Réessayez dans un instant.',
    );

    screen.querySelector<HTMLButtonElement>('[data-retry]')!.click();
    const games = library();
    await server.answer(LIBRARY_FIRST_PAGE, { total: games.length, offset: 0, limit: 500, games });
    await harness.fixture.whenStable();

    expect(names(screen)).toHaveLength(4);
  });

  it('speaks English too', async () => {
    asList();
    const { screen } = await open(library(), 'en');

    expect(text(screen, '[data-owned]')).toBe('4 games, from Steam');
    expect(text(row(screen, 'Portal 2'), '[data-last-played]')).toBe('Yesterday');
    expect(text(row(screen, 'Celeste'), '[data-last-played]')).toBe('Never launched');
    expect(text(screen, '[data-shown]')).toBe('4 games shown out of 4');
  });
});
