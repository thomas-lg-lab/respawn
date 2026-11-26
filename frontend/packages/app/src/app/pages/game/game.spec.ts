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
    expect(screen.querySelector('[data-cover] img')?.getAttribute('src')).toBe(
      `/api/v1/games/${ID}/cover`,
    );
    // Opened from its address: it leads back to the library.
    expect(text(screen, '[data-back]')).toBe('Bibliothèque');
    expect(screen.querySelector('[data-back]')?.getAttribute('href')).toBe('/library');
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
    const back = vi.spyOn(location, 'back');
    screen.querySelector<HTMLAnchorElement>('[data-back]')!.click();
    expect(back).toHaveBeenCalled();
  });
});
