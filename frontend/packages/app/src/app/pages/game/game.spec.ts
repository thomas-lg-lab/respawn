import { Location } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import type { Game, GameMonth, LibraryEntry, LibraryStats, LibraryYears } from '@respawn/data';
import { TEST_LIBRARY_STATS, TEST_LINKED_ACCOUNT } from '@respawn/data/testing';
import { ACCOUNT, api, button, start } from '@app/testing/app-testing';
import { ago } from '@app/testing/games';
import '../../layout/shell';
import './game';

const HOUR = 3600;
const ID = '0198a1b2-0000-7000-8000-000000000001';

/** This month and the ones before it, as the API names them, in UTC. */
const today = new Date();
const YEAR = today.getUTCFullYear();
const key = (year: number, month: number) => `${year}-${String(month).padStart(2, '0')}`;
const THIS_MONTH = key(YEAR, today.getUTCMonth() + 1);

function entry(values: Partial<LibraryEntry> = {}): LibraryEntry {
  return {
    store_entry_id: '0198a1b2-0000-7000-8000-0000000000e1',
    linked_account_id: TEST_LINKED_ACCOUNT.id,
    source: 'steam',
    external_id: '1245620',
    playtime_seconds: 142 * HOUR,
    recent_playtime_seconds: 6 * HOUR,
    last_played_at: ago(1),
    first_seen_at: '2026-11-21T04:00:00Z',
    as_of: ago(0),
    missing_since: null,
    ...values,
  };
}

/** Elden Ring, as its owner sees it: third of the games launched, played yesterday. */
function eldenRing(values: Partial<Game> = {}): Game {
  return {
    game_id: ID,
    name: 'Elden Ring',
    stores: [{ store_entry_id: entry().store_entry_id, source: 'steam', external_id: '1245620' }],
    playtime_seconds: 142 * HOUR,
    recent_playtime_seconds: 6 * HOUR,
    last_played_at: ago(1),
    entries: [entry()],
    rank: { position: 3, launched: 457 },
    removed: null,
    game_data: { source: 'igdb', url: 'https://www.igdb.com/games/elden-ring' },
    covers: { sources: ['steam'], chosen: null },
    ...values,
  };
}

/** March two years ago, its best month; and 6 h this month. */
function months(): GameMonth[] {
  return [
    { month: `${key(YEAR - 2, 3)}-01`, seconds: 21 * HOUR, source: 'review' },
    { month: `${key(YEAR - 2, 4)}-01`, seconds: 14 * HOUR, source: 'review' },
    { month: `${THIS_MONTH}-01`, seconds: 6 * HOUR, source: 'readings' },
  ];
}

/** The two years before from a review, whole; this year counted since the first sync. */
const YEARS: LibraryYears = {
  years: [
    ...[YEAR - 2, YEAR - 1].map((year) => ({
      year,
      seconds: 600 * HOUR,
      source: 'review',
      first_day: `${year}-01-01`,
      last_day: `${year}-12-31`,
      whole: true,
    })),
    {
      year: YEAR,
      seconds: 38 * HOUR,
      source: 'readings',
      first_day: `${THIS_MONTH}-01`,
      last_day: `${THIS_MONTH}-02`,
      whole: false,
    },
  ],
};

const STATS: LibraryStats = { ...TEST_LIBRARY_STATS, recent_playtime_seconds: 14 * HOUR };

/** This month: two other games, the page's in the middle. */
function thisMonth() {
  return {
    month: `${THIS_MONTH}-01`,
    source: 'readings',
    first_day: `${THIS_MONTH}-01`,
    last_day: `${THIS_MONTH}-02`,
    games: [
      { game_id: 'a', name: 'Factorio', seconds: 22 * HOUR },
      { game_id: ID, name: 'Elden Ring', seconds: 6 * HOUR },
      { game_id: 'b', name: 'Balatro', seconds: 5 * HOUR },
    ],
  };
}

describe('Game', () => {
  /** Opens the page of a game, signed in, the API answering what it asks on the way. */
  async function open(game: Game | null = eldenRing(), played: GameMonth[] = months()) {
    const { harness, server, arrived } = await start(`/games/${ID}`);
    await server.answer('/me', ACCOUNT);
    await arrived;
    await server.answer('/linked-accounts', { accounts: [TEST_LINKED_ACCOUNT] });
    // Left unanswered, a request keeps the app busy: the spec answers it, then waits.
    if (game) {
      await answerAll(server, game, played);
      await harness.fixture.whenStable();
    }
    return { harness, server, screen: harness.fixture.nativeElement as HTMLElement };
  }

  /** Everything the page shows, asked again after a change: the game, its months, the library's. */
  async function answerAll(server: ReturnType<typeof api>, game: Game, played = months()) {
    await server.answer(`/games/${ID}`, game);
    await server.answer(`/games/${ID}/months`, { months: played });
    await server.answer('/library/years', YEARS);
    await server.answer('/library/stats', STATS);
    if (played.length) await server.answer(`/library/months/${THIS_MONTH}`, thisMonth());
  }

  const text = (within: Element, selector: string) =>
    within.querySelector(selector)?.textContent.replace(/\s+/g, ' ').trim();
  /** A figure's three parts, each read on its own: what it counts, the figure, what it covers. */
  const figure = (screen: HTMLElement, name: string) =>
    [...screen.querySelectorAll(`[data-figure="${name}"] span`)]
      .map((part) => part.textContent.replace(/\s+/g, ' ').trim())
      .join(' | ');

  beforeEach(() => localStorage.clear());
  afterEach(() => api().http.verify());

  it('says the game, where it stands, and the way to it on Steam', async () => {
    const { screen } = await open();

    expect(text(screen, 'h1')).toBe('Elden Ring');
    expect(text(screen, '[data-status]')).toBe('Dernière session hier');
    expect(screen.querySelector('[data-status] [data-recency]')?.getAttribute('data-recency')).toBe(
      '1',
    );
    expect(screen.querySelector('[data-launch]')?.getAttribute('href')).toBe('steam://run/1245620');
    expect(screen.querySelector('[data-store]')?.getAttribute('href')).toBe(
      'https://store.steampowered.com/app/1245620/',
    );
    expect(text(screen, '[data-store]')).toBe('Steam');
    expect(screen.querySelector('[data-store]')?.getAttribute('aria-label')).toBe('Page Steam');
    expect(screen.querySelector('[data-status-line] [data-store]')).not.toBeNull();
    expect(text(screen, '[data-game-data]')).toBe('IGDB');
    expect(screen.querySelector('[data-game-data]')?.getAttribute('aria-label')).toBe('Page IGDB');
    // Both quiet, on the status line: not among the page's actions.
    expect(screen.querySelector('[data-status-line] [data-game-data]')).not.toBeNull();
    expect(screen.querySelector('[data-game-data]')?.getAttribute('href')).toBe(
      'https://www.igdb.com/games/elden-ring',
    );
    expect(screen.querySelector('[data-cover] img')?.getAttribute('src')).toBe(
      `/api/v1/games/${ID}/cover`,
    );
    // Opened from its address: it leads back to the library.
    expect(text(screen, '[data-back]')).toBe('Bibliothèque');
    expect(screen.querySelector('[data-back]')?.getAttribute('href')).toBe('/library');
  });

  it('leads to IGDB only where IGDB knows the game, on its own site', async () => {
    const unknown = await open(eldenRing({ game_data: null }));
    expect(unknown.screen.querySelector('[data-game-data]')).toBeNull();
    expect(unknown.screen.querySelector('[data-store]')).not.toBeNull();
  });

  it('never leads elsewhere for IGDB', async () => {
    const { screen } = await open(
      eldenRing({ game_data: { source: 'igdb', url: 'https://elsewhere.example.org/games/x' } }),
    );
    expect(screen.querySelector('[data-game-data]')).toBeNull();
  });

  it('offers to choose the cover only where the game has more than one', async () => {
    const { screen } = await open();
    expect(screen.querySelector('[data-cover-button]')).toBeNull();
    expect(screen.querySelector('[data-cover]')).not.toBeNull();
  });

  it('chooses IGDB’s cover for the account, and goes back to Steam’s', async () => {
    const two = { sources: ['steam', 'igdb'], chosen: null };
    const { harness, server, screen } = await open(eldenRing({ covers: two }));

    // The cover is the button, named for what it does; the choice is hidden until it is pressed.
    const cover = screen.querySelector<HTMLButtonElement>('[data-cover-button]')!;
    expect(cover.getAttribute('aria-label')).toBe('Changer la jaquette');
    expect(cover.querySelector('img')?.getAttribute('src')).toBe(`/api/v1/games/${ID}/cover`);
    expect(screen.querySelector<HTMLElement>('[data-cover-panel]')!.hidden).toBe(true);

    cover.click();
    await harness.fixture.whenStable();
    const radios = [...screen.querySelectorAll<HTMLButtonElement>('[data-cover-source]')];
    expect(
      radios.map((radio) => [radio.textContent.trim(), radio.getAttribute('aria-checked')]),
    ).toEqual([
      ['Steam', 'true'],
      ['IGDB', 'false'],
    ]);
    const igdb = screen.querySelector<HTMLButtonElement>('[data-cover-source="igdb"]')!;
    expect(igdb.querySelector('img')?.getAttribute('src')).toBe(
      `/api/v1/games/${ID}/cover?source=igdb`,
    );
    expect(text(screen, '[data-cover-hint]')).toBe(
      'Celle de Steam par défaut. Pour vous uniquement.',
    );

    igdb.click();
    const chosen = await server.request(`/games/${ID}/cover-choice`);
    expect(chosen.request.method).toBe('PUT');
    expect(chosen.request.body).toEqual({ source: 'igdb' });
    chosen.flush({ sources: ['steam', 'igdb'], chosen: 'igdb' });
    await harness.fixture.whenStable();

    expect(cover.querySelector('img')?.getAttribute('src')).toBe(
      `/api/v1/games/${ID}/cover?source=igdb`,
    );
    cover.click();
    await harness.fixture.whenStable();
    expect(text(screen, '[data-cover-hint]')).toBe(
      'Pour vous uniquement. Revenir à celle de Steam',
    );

    screen.querySelector<HTMLButtonElement>('[data-cover-reset]')!.click();
    const forgotten = await server.request(`/games/${ID}/cover-choice`);
    expect(forgotten.request.method).toBe('DELETE');
    forgotten.flush(two);
    await harness.fixture.whenStable();
    expect(cover.querySelector('img')?.getAttribute('src')).toBe(`/api/v1/games/${ID}/cover`);
  });

  it('says a cover could not be chosen', async () => {
    const { harness, server, screen } = await open(
      eldenRing({ covers: { sources: ['steam', 'igdb'], chosen: null } }),
    );

    screen.querySelector<HTMLButtonElement>('[data-cover-button]')!.click();
    await harness.fixture.whenStable();
    screen.querySelector<HTMLButtonElement>('[data-cover-source="igdb"]')!.click();
    await server.refuse(`/games/${ID}/cover-choice`, 409, 'no_such_cover');
    await harness.fixture.whenStable();

    expect(text(screen, '[data-cover-refused]')).toBe(
      'La jaquette n’a pas pu être changée. Réessayez.',
    );
  });

  it('gives its four figures, each with what it is measured against', async () => {
    const { screen } = await open();

    expect(figure(screen, 'total')).toBe('Temps total | 142 h | 3e de vos 457 jeux lancés');
    // This year is counted since the first sync: the figure says from when.
    expect(figure(screen, 'year')).toMatch(/^Cette année \| 6 h \| compté depuis le 1 /);
    expect(figure(screen, 'recent')).toBe('Ces deux semaines | 6 h | sur 14 h, soit 43 %');
    expect(figure(screen, 'best')).toBe(`Meilleur mois | 21 h | mars ${YEAR - 2}`);
  });

  it('draws its months, and says what else was played in the last one', async () => {
    const { screen } = await open();

    const years = [...screen.querySelectorAll('[data-year]')].map((row) =>
      row.getAttribute('data-year'),
    );
    expect(years).toEqual([String(YEAR - 2), String(YEAR)]);
    expect(
      [...screen.querySelectorAll('[data-gap] span')].map((part) => part.textContent.trim()),
    ).toEqual([String(YEAR - 1), '', 'aucune session']);
    const best = screen.querySelector(`[data-month="${key(YEAR - 2, 3)}"]`)!;
    expect(best.getAttribute('data-shade')).toBe('4');
    expect(best.getAttribute('aria-label')).toBe(`mars ${YEAR - 2} : 21 h`);

    // The last month played is the one shown at first.
    expect(screen.querySelector(`[data-month="${THIS_MONTH}"]`)?.getAttribute('aria-pressed')).toBe(
      'true',
    );
    expect(text(screen, '[data-month-time]')).toBe('6 h');
    expect(text(screen, '[data-month-place]')).toBe(
      '2e des 3 jeux joués ce mois-là · 18 % de vos 33 h',
    );
    expect(
      [...screen.querySelectorAll('[data-month-games] [data-name]')].map((each) =>
        each.textContent.trim(),
      ),
    ).toEqual(['Factorio', 'Elden Ring', 'Balatro']);
    // The other games lead to their pages; the page's own is no link to itself.
    expect(
      [...screen.querySelectorAll('[data-month-games] [data-name]')].map((each) =>
        each.getAttribute('href'),
      ),
    ).toEqual(['/games/a', null, '/games/b']);
  });

  it('says another month once it is pressed', async () => {
    const { harness, server, screen } = await open();

    screen.querySelector<HTMLButtonElement>(`[data-month="${key(YEAR - 2, 3)}"]`)!.click();
    await server.answer(`/library/months/${key(YEAR - 2, 3)}`, {
      ...thisMonth(),
      games: [{ game_id: ID, name: 'Elden Ring', seconds: 21 * HOUR }],
    });
    await harness.fixture.whenStable();

    expect(text(screen, '[data-month-name]')).toBe(`mars ${YEAR - 2}`);
    expect(text(screen, '[data-month-time]')).toBe('21 h');
    expect(text(screen, '[data-month-place]')).toBe('Le seul jeu joué ce mois-là');
  });

  it('says a game never launched has no time yet, and draws no figure', async () => {
    const never = eldenRing({
      playtime_seconds: 0,
      recent_playtime_seconds: 0,
      last_played_at: null,
      rank: null,
      entries: [entry({ playtime_seconds: 0, recent_playtime_seconds: 0, last_played_at: null })],
    });
    const { screen } = await open(never, []);

    expect(text(screen, '[data-status]')).toBe('Jamais lancé');
    expect(screen.querySelector('[data-never]')).not.toBeNull();
    expect(screen.querySelector('[data-figures]')).toBeNull();
    expect(screen.querySelector('[data-calendar]')).toBeNull();
  });

  it('takes a game its store no longer lists out of the library, and puts it back', async () => {
    const missing = eldenRing({ entries: [entry({ missing_since: '2026-11-12T04:00:00Z' })] });
    const { harness, server, screen } = await open(missing);

    expect(text(screen, '[data-notice="missing"]')).toContain(
      'Steam ne liste plus ce jeu dans votre bibliothèque depuis le 12 nov. 2026.',
    );
    expect(text(screen, '[data-status]')).toBe('Absent de votre bibliothèque');
    // No copy is listed any more: there is none to launch.
    expect(screen.querySelector('[data-launch]')).toBeNull();

    button(screen, 'Retirer de ma bibliothèque').click();
    const takenOut = await server.request(`/library/games/${ID}`);
    expect(takenOut.request.method).toBe('DELETE');
    takenOut.flush(null, { status: 204, statusText: '' });
    const removed = {
      game_id: ID,
      name: 'Elden Ring',
      back_since: null,
      entries: [],
    };
    await answerAll(server, eldenRing({ entries: [], rank: null, removed }));
    await harness.fixture.whenStable();

    expect(text(screen, '[data-notice="removed"]')).toContain('Jeu retiré de votre bibliothèque.');
    button(screen, 'Annuler').click();
    const putBack = await server.request(`/library/games/${ID}`);
    expect(putBack.request.method).toBe('PUT');
    putBack.flush(null, { status: 204, statusText: '' });
    await answerAll(server, missing);
    await harness.fixture.whenStable();

    expect(screen.querySelector('[data-notice="missing"]')).not.toBeNull();
  });

  it('says why a game could not be taken out', async () => {
    const missing = eldenRing({ entries: [entry({ missing_since: '2026-11-12T04:00:00Z' })] });
    const { harness, server, screen } = await open(missing);

    button(screen, 'Retirer de ma bibliothèque').click();
    await server.refuse(`/library/games/${ID}`, 409, 'still_listed');
    await harness.fixture.whenStable();

    expect(text(screen, '[data-refused]')).toBe(
      'Steam liste toujours ce jeu : seul un jeu qu’il ne liste plus peut être retiré.',
    );
  });

  it('says a game taken out is listed again, and leaves it out on request', async () => {
    const back = eldenRing({
      entries: [],
      rank: null,
      removed: { game_id: ID, name: 'Elden Ring', back_since: '2026-11-20T04:00:00Z', entries: [] },
    });
    const { harness, server, screen } = await open(back);

    expect(text(screen, '[data-notice="back"]')).toContain(
      'Steam liste de nouveau ce jeu depuis le 20 nov. 2026.',
    );
    button(screen, 'Le laisser retiré').click();
    const dismissed = await server.request(`/library/removed/${ID}/notice`);
    expect(dismissed.request.method).toBe('DELETE');
    dismissed.flush(null, { status: 204, statusText: '' });
    await server.answer(`/games/${ID}`, {
      ...back,
      removed: { ...back.removed!, back_since: null },
    });
    // Its months are the game's too, asked again with it.
    await server.answer(`/games/${ID}/months`, { months: months() });
    await harness.fixture.whenStable();

    expect(screen.querySelector('[data-notice="back"]')).toBeNull();
    expect(screen.querySelector('[data-notice="removed"]')).not.toBeNull();
  });

  it('says a game is unknown', async () => {
    const { harness, server, screen } = await open(null);
    await server.refuse(`/games/${ID}`, 404, 'unknown_game');
    await server.answer(`/games/${ID}/months`, { months: [] });
    await server.answer('/library/years', YEARS);
    await server.answer('/library/stats', STATS);
    await harness.fixture.whenStable();

    expect(text(screen, 'h1')).toBe('Ce jeu est introuvable');
  });

  it('names the screen it was reached from, and leads back to it as it was', async () => {
    const { harness, server } = await open();
    const router = TestBed.inject(Router);
    const location = TestBed.inject(Location);

    await router.navigateByUrl('/time');
    await server.answer('/library?sort=name&offset=0&limit=500', {
      total: 0,
      offset: 0,
      limit: 500,
      games: [],
    });
    await harness.fixture.whenStable();
    await router.navigate(['/games', ID], { state: { from: 'time' } });
    await harness.fixture.whenStable();
    const screen = harness.fixture.nativeElement as HTMLElement;

    expect(text(screen, '[data-back]')).toBe('Temps');
    expect(screen.querySelector('[data-back]')?.getAttribute('href')).toBe('/time');
    const back = vi.spyOn(location, 'back');
    const navigate = vi.spyOn(router, 'navigateByUrl');
    screen.querySelector<HTMLAnchorElement>('[data-back]')!.click();
    expect(back).toHaveBeenCalledOnce();
    // The browser's way back alone: no second navigation to the screen's bare address.
    expect(navigate).not.toHaveBeenCalled();
  });

  it('leaves a press that opens a new tab to the browser', async () => {
    const { screen } = await open();
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl');

    // What the page did with the press is read once it is through; the test's document then
    // stays where it is, as it cannot open a window.
    let prevented: boolean | undefined;
    screen.addEventListener('click', (event) => {
      prevented = event.defaultPrevented;
      event.preventDefault();
    });
    const press = new MouseEvent('click', { bubbles: true, cancelable: true, shiftKey: true });
    screen.querySelector('[data-back]')!.dispatchEvent(press);

    expect(prevented).toBe(false);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('says its own origin and its own refusals for another game opened from it', async () => {
    const missing = eldenRing({ entries: [entry({ missing_since: '2026-11-12T04:00:00Z' })] });
    const { harness, server } = await open(missing);
    const router = TestBed.inject(Router);
    const location = TestBed.inject(Location);

    // Reached from the time map, and refused a take-out.
    await router.navigateByUrl('/time');
    await server.answer('/library?sort=name&offset=0&limit=500', {
      total: 0,
      offset: 0,
      limit: 500,
      games: [],
    });
    await harness.fixture.whenStable();
    await router.navigate(['/games', ID], { state: { from: 'time' } });
    await harness.fixture.whenStable();
    const screen = harness.fixture.nativeElement as HTMLElement;
    expect(text(screen, '[data-back]')).toBe('Temps');
    button(screen, 'Retirer de ma bibliothèque').click();
    await server.refuse(`/library/games/${ID}`, 409, 'still_listed');
    await harness.fixture.whenStable();
    expect(screen.querySelector('[data-refused]')).not.toBeNull();

    // Another game, chosen in the search of this page: the same page, with nothing of the first.
    const OTHER = '0198a1b2-0000-7000-8000-000000000002';
    await router.navigate(['/games', OTHER]);
    await server.answer(`/games/${OTHER}`, { ...missing, game_id: OTHER, name: 'Hades' });
    await server.answer(`/games/${OTHER}/months`, { months: months() });
    await harness.fixture.whenStable();

    expect(text(screen, 'h1')).toBe('Hades');
    expect(screen.querySelector('[data-refused]')).toBeNull();
    expect(text(screen, '[data-back]')).toBe('Bibliothèque');
    const back = vi.spyOn(location, 'back');
    // The library's games were read on the time map: the Library asks nothing more.
    screen.querySelector<HTMLAnchorElement>('[data-back]')!.click();
    await harness.fixture.whenStable();
    expect(back).not.toHaveBeenCalled();
    expect(location.path()).toBe('/library');
  });

  it('draws no month until the library says which ones were counted', async () => {
    const { harness, server, screen } = await open(null);
    await server.answer(`/games/${ID}`, eldenRing());
    await server.answer(`/games/${ID}/months`, { months: months() });
    await server.refuse('/library/years', 503, 'database_unavailable');
    await server.answer('/library/stats', STATS);
    await harness.fixture.whenStable();

    expect(screen.querySelector('[data-calendar]')).toBeNull();
    expect(text(screen, '[data-months-refused]')).toBe(
      'La base de données ne répond pas. Réessayez dans un instant.',
    );

    button(screen, 'Réessayer').click();
    await server.answer('/library/years', YEARS);
    await server.answer(`/library/months/${THIS_MONTH}`, thisMonth());
    await harness.fixture.whenStable();
    expect(screen.querySelector('[data-calendar]')).not.toBeNull();
  });
});
