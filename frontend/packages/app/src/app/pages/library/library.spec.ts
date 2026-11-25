import { TestBed } from '@angular/core/testing';
import { RouterTestingHarness } from '@angular/router/testing';
import { type LibraryGame, TEST_LINKED_ACCOUNT, TEST_SYNC_RUN } from '@respawn/data';
import { QueryClient } from '@tanstack/angular-query';
import { ACCOUNT, api, button, configureApp, type } from '@app/testing/app-testing';
import type { Language } from '@app/i18n/language';
import '../../layout/shell';
import './library';

const FIRST_PAGE = '/library?sort=name&offset=0&limit=500';

let made = 0;

function game(name: string, values: Partial<LibraryGame> = {}): LibraryGame {
  return {
    game_id: `00000000-0000-4000-8000-${String(++made).padStart(12, '0')}`,
    name,
    playtime_seconds: 0,
    recent_playtime_seconds: 0,
    last_played_at: null,
    entries: [],
    ...values,
  };
}

/** Noon, that many days back on the calendar: a clock change cannot move it to another day. */
function ago(days: number): string {
  const moment = new Date();
  moment.setDate(moment.getDate() - days);
  moment.setHours(12, 0, 0, 0);
  return moment.toISOString();
}

/** A small library, as the API gives it: by title. */
function library(): LibraryGame[] {
  return [
    game('Celeste'),
    game('Hades', { playtime_seconds: 360000, last_played_at: ago(400) }),
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
  async function open(games: LibraryGame[] | null = library(), language: Language = 'fr') {
    configureApp(language);
    const harness = await RouterTestingHarness.create();
    const server = api();
    const arrived = harness.navigateByUrl('/library');
    await server.answer('/me', ACCOUNT);
    await arrived;
    // The shell asks for the linked accounts on every screen: where the end of a sync is learnt.
    await server.answer('/linked-accounts', { accounts: [TEST_LINKED_ACCOUNT] });
    // Left unanswered, the request keeps the app busy: the spec answers it, then waits.
    if (games) {
      await server.answer(FIRST_PAGE, { total: games.length, offset: 0, limit: 500, games });
      await harness.fixture.whenStable();
    }
    return { harness, server, screen: harness.fixture.nativeElement as HTMLElement };
  }

  const text = (within: Element, selector: string) =>
    within.querySelector(selector)?.textContent.replace(/\s+/g, ' ').trim();
  const names = (screen: HTMLElement) =>
    [...screen.querySelectorAll('[data-name]')].map((each) => each.textContent);
  const row = (screen: HTMLElement, name: string) =>
    [...screen.querySelectorAll('tbody tr')].find((each) => text(each, '[data-name]') === name)!;
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

  it('reads the games again once a sync ended, learnt on this screen', async () => {
    const { harness, server, screen } = await open();

    // The window is back in front, say: the accounts are asked again, and a sync ended since.
    void TestBed.inject(QueryClient).invalidateQueries({ queryKey: ['linked-accounts'] });
    const last = { ...TEST_SYNC_RUN, id: '6a1d2e3f-4b5c-4d6e-8f70-1a2b3c4d5e6f' };
    await server.answer('/linked-accounts', {
      accounts: [{ ...TEST_LINKED_ACCOUNT, sync: { ...TEST_LINKED_ACCOUNT.sync, last } }],
    });
    const games = [...library(), game('Outer Wilds')];
    await server.answer(FIRST_PAGE, { total: games.length, offset: 0, limit: 500, games });
    await harness.fixture.whenStable();

    expect(names(screen)).toContain('Outer Wilds');
  });

  it('says a playtime, the last two weeks and the last session of a game', async () => {
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
    const { screen } = await open();

    expect(text(screen, 'caption')).toBe('Bibliothèque');
    expect(text(row(screen, 'Portal 2'), '[data-last-played-under]')).toBe('Hier');
    expect(text(row(screen, 'Celeste'), '[data-last-played-under]')).toBe('Jamais lancé');
    // Nothing to say under a title when the store did not say.
    expect(row(screen, 'Pokémon').querySelector('[data-last-played-under]')).toBeNull();
  });

  it('tells a game never launched from one the store says nothing about', async () => {
    const { screen } = await open();

    expect(text(row(screen, 'Celeste'), '[data-playtime]')).toBe('0 min');
    expect(text(row(screen, 'Celeste'), '[data-last-played]')).toBe('Jamais lancé');
    expect(text(row(screen, 'Pokémon'), '[data-playtime]')).toBe('—');
    expect(text(row(screen, 'Pokémon'), '[data-recent]')).toBe('—');
    expect(text(row(screen, 'Pokémon'), '[data-last-played]')).toBe('—');
  });

  it('searches by title, and counts what each filter would keep of it', async () => {
    const { harness, screen } = await open();

    type(screen.querySelector('input[type="search"]')!, 'po');
    await harness.fixture.whenStable();

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

  it('says that no game matches, the search still there to change', async () => {
    const { harness, screen } = await open();

    type(screen.querySelector('input[type="search"]')!, 'zelda');
    await harness.fixture.whenStable();

    expect(text(screen, '[data-no-match]')).toBe('Aucun jeu ne correspond.');
    expect(screen.querySelector('table')).toBeNull();
    expect(screen.querySelector('input[type="search"]')).not.toBeNull();
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

    type(screen.querySelector('input[type="search"]')!, 'game');
    await harness.fixture.whenStable();
    expect(names(screen)).toHaveLength(100);
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
    expect(screen.querySelector('input[type="search"]')).toBeNull();
  });

  it('says that the games are on their way', async () => {
    const { harness, server, screen } = await open(null);

    await vi.waitFor(() =>
      expect(text(screen, 'main [role="status"]')).toBe('Chargement de vos jeux…'),
    );

    await server.answer(FIRST_PAGE, { total: 0, offset: 0, limit: 500, games: [] });
    await harness.fixture.whenStable();
  });

  it('says why the games could not be read, and asks again on request', async () => {
    const { harness, server, screen } = await open(null);
    await server.refuse(FIRST_PAGE, 503, 'database_unavailable');
    await harness.fixture.whenStable();

    expect(text(screen, '[role="alert"]')).toBe(
      'La base de données ne répond pas. Réessayez dans un instant.',
    );

    screen.querySelector<HTMLButtonElement>('[data-retry]')!.click();
    const games = library();
    await server.answer(FIRST_PAGE, { total: games.length, offset: 0, limit: 500, games });
    await harness.fixture.whenStable();

    expect(names(screen)).toHaveLength(4);
  });

  it('speaks English too', async () => {
    const { screen } = await open(library(), 'en');

    expect(text(screen, '[data-owned]')).toBe('4 games, from Steam');
    expect(text(row(screen, 'Portal 2'), '[data-last-played]')).toBe('Yesterday');
    expect(text(row(screen, 'Celeste'), '[data-last-played]')).toBe('Never launched');
    expect(text(screen, '[data-shown]')).toBe('4 games shown out of 4');
  });
});
