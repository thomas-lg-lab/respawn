import { Location } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import type {
  Game,
  GameData,
  GameMonth,
  LibraryEntry,
  LibraryStats,
  LibraryYears,
} from '@respawn/data';
import { TEST_LIBRARY_STATS, TEST_LINKED_ACCOUNT } from '@respawn/data/testing';
import { ACCOUNT, LIBRARY_FIRST_PAGE, api, button, start, text } from '@app/testing/app-testing';
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

/** What IGDB says of Elden Ring: five genres and themes, and how long it takes. */
function igdb(values: Partial<GameData> = {}): GameData {
  return {
    source: 'igdb',
    url: 'https://www.igdb.com/games/elden-ring',
    genres: ['Role-playing (RPG)', 'Adventure'],
    themes: ['Action', 'Fantasy', 'Open world'],
    summary: 'An action RPG in a vast open world.',
    time_to_beat: {
      hastily_seconds: 58 * HOUR,
      normally_seconds: 100 * HOUR,
      completely_seconds: 133 * HOUR,
      count: 1240,
    },
    released_on: '2022-02-25',
    similar: [],
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
    game_data: igdb(),
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
    // Words only: no square beside them.
    expect(screen.querySelector('[data-status] [data-recency]')).toBeNull();
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
      eldenRing({ game_data: igdb({ url: 'https://elsewhere.example.org/games/x' }) }),
    );
    expect(screen.querySelector('[data-game-data]')).toBeNull();
  });

  it('shows its first genres in the hero, each leading to the library narrowed to it', async () => {
    const { harness, screen } = await open();

    const shown = [...screen.querySelectorAll('[data-hero] [data-tag]')];
    expect(shown.map((tag) => tag.textContent.trim())).toEqual([
      'Role-playing (RPG)',
      'Adventure',
      'Action',
      'Fantasy',
    ]);
    // IGDB's words, in English, said as such.
    expect(shown[0]?.getAttribute('lang')).toBe('en');
    expect(shown[0]?.getAttribute('href')).toBe('/library?genre=Role-playing%20(RPG)');
    // The last pill shows the fifth in place, and folds it again.
    const more = screen.querySelector<HTMLButtonElement>('[data-more-tags]')!;
    expect(more.textContent.trim()).toBe('+ 1');
    expect(more.getAttribute('aria-label')).toBe('Voir tous les genres et thèmes, 1 de plus');
    expect(more.getAttribute('aria-expanded')).toBe('false');
    more.click();
    await harness.fixture.whenStable();
    expect(screen.querySelectorAll('[data-hero] [data-tag]')).toHaveLength(5);
    expect(more.textContent.trim()).toBe('Moins');
    expect(more.getAttribute('aria-expanded')).toBe('true');
    more.click();
    await harness.fixture.whenStable();
    expect(screen.querySelectorAll('[data-hero] [data-tag]')).toHaveLength(4);
  });

  it('says what IGDB says the game is, with all its genres and where it comes from', async () => {
    const { screen } = await open();

    expect(text(screen, '[data-about] h2')).toBe('À propos');
    expect(text(screen, '[data-summary]')).toBe('An action RPG in a vast open world.');
    expect(screen.querySelector('[data-summary]')?.getAttribute('lang')).toBe('en');
    expect(screen.querySelectorAll('[data-about] [data-tag]')).toHaveLength(5);
    expect(text(screen, '[data-about-source]')).toBe('Données du jeu : IGDB');
    expect(screen.querySelector('[data-about-source]')?.getAttribute('href')).toBe(
      'https://www.igdb.com/games/elden-ring',
    );
  });

  it('sets its time against how long players say it takes', async () => {
    const { screen } = await open();

    expect(text(screen, '[data-you]')).toBe('Vous · 142 h');
    const pace = (name: string) => {
      const row = screen.querySelector(`[data-pace="${name}"]`)!;
      return [
        row.getAttribute('data-state'),
        text(row, '[data-pace-words]'),
        text(row, '[data-pace-time]'),
        text(row, '[data-pace-state]'),
      ];
    };
    expect(pace('hastily')).toEqual(['reached', 'En allant vite', '58 h', 'atteint']);
    expect(pace('normally')).toEqual(['reached', 'Normalement', '100 h', 'atteint']);
    // Past the longest pace, by how much.
    expect(pace('completely')).toEqual(['beyond', 'À 100 %', '133 h', '+ 9 h']);
    // One scale: the time played as far as it goes, a mark where each pace stands on it.
    const scale = screen.querySelector<HTMLElement>('[data-durations] [data-scale]')!;
    const marks = [...scale.querySelectorAll<HTMLElement>('span')].map(
      (mark) => mark.style.cssText,
    );
    expect(marks).toEqual(['width: 100%;', 'left: 40.8451%;', 'left: 70.4225%;', 'left: 93.662%;']);
    expect(text(screen, '[data-durations-source]')?.replace(/\s/g, ' ')).toBe(
      'D’après 1 240 joueurs, sur IGDB',
    );
  });

  it('says what is left of each pace, and only the paces players gave', async () => {
    const { screen } = await open(
      eldenRing({
        playtime_seconds: 70 * HOUR,
        game_data: igdb({
          time_to_beat: {
            hastily_seconds: 58 * HOUR,
            normally_seconds: 100 * HOUR,
            completely_seconds: null,
            count: null,
          },
        }),
      }),
    );

    expect(
      [...screen.querySelectorAll('[data-pace]')].map((row) => row.getAttribute('data-state')),
    ).toEqual(['reached', 'left']);
    expect(text(screen, '[data-pace="normally"]')).toContain('reste 30 h');
    expect(text(screen, '[data-durations-source]')).toBe('Sur IGDB');
  });

  it('says how far past the longest pace players gave, even short of 100 %', async () => {
    const { screen } = await open(
      eldenRing({
        playtime_seconds: 130 * HOUR,
        game_data: igdb({
          time_to_beat: {
            hastily_seconds: 58 * HOUR,
            normally_seconds: 100 * HOUR,
            completely_seconds: null,
            count: null,
          },
        }),
      }),
    );

    expect(
      [...screen.querySelectorAll('[data-pace]')].map((row) => row.getAttribute('data-state')),
    ).toEqual(['reached', 'beyond']);
    expect(text(screen, '[data-pace="normally"]')).toContain('+ 30 h');
  });

  it('gives a game never launched how long it takes, with nothing set against it', async () => {
    const never = eldenRing({
      playtime_seconds: 0,
      recent_playtime_seconds: 0,
      last_played_at: null,
      rank: null,
      entries: [entry({ playtime_seconds: 0, recent_playtime_seconds: 0, last_played_at: null })],
    });
    const { screen } = await open(never, []);

    expect(screen.querySelectorAll('[data-pace]')).toHaveLength(3);
    expect(screen.querySelector('[data-you]')).toBeNull();
    expect(screen.querySelector('[data-durations] [data-scale]')).toBeNull();
    expect(
      [...screen.querySelectorAll('[data-pace]')].map((row) => row.getAttribute('data-state')),
    ).toEqual(['none', 'none', 'none']);
  });

  it('shows nothing of IGDB where IGDB says nothing of the game', async () => {
    const { screen } = await open(eldenRing({ game_data: null }));

    expect(screen.querySelector('[data-tags]')).toBeNull();
    // No row is kept in the hero for the genres it does not have.
    expect(screen.querySelector('[data-hero]')?.hasAttribute('data-tagged')).toBe(false);
    expect(screen.querySelector('[data-about]')).toBeNull();
    expect(screen.querySelector('[data-durations]')).toBeNull();
    expect(screen.querySelector('[data-similar]')).toBeNull();
  });

  it('says the month it came out, beside where its data comes from', async () => {
    const { screen } = await open(eldenRing());

    expect(text(screen, '[data-released]')).toBe('Sorti en février 2022');
    expect(screen.querySelector('[data-about] [data-about-source]')).not.toBeNull();
  });

  it('says nothing of when it came out when IGDB does not', async () => {
    const { screen } = await open(eldenRing({ game_data: igdb({ released_on: null }) }));

    expect(screen.querySelector('[data-released]')).toBeNull();
  });

  it('lists the games of the library close to it, each leading to its page', async () => {
    const sekiro = '0198a1b2-0000-7000-8000-0000000000a1';
    const hades = '0198a1b2-0000-7000-8000-0000000000a2';
    const unnamed = '0198a1b2-0000-7000-8000-0000000000a3';
    const { screen } = await open(
      eldenRing({
        game_data: igdb({
          similar: [
            {
              game_id: sekiro,
              name: 'Sekiro',
              playtime_seconds: 48 * HOUR,
              last_played_at: ago(400),
              cover_source: null,
              normally_seconds: 30 * HOUR,
            },
            {
              game_id: hades,
              name: 'Hades',
              playtime_seconds: 0,
              last_played_at: null,
              cover_source: 'igdb',
              normally_seconds: 22 * HOUR,
            },
            {
              game_id: unnamed,
              name: null,
              playtime_seconds: 0,
              last_played_at: null,
              cover_source: null,
              normally_seconds: null,
            },
          ],
        }),
      }),
    );

    const rows = [...screen.querySelectorAll('[data-similar] li')];
    expect(rows.map((row) => row.getAttribute('data-game'))).toEqual([sekiro, hades, unnamed]);
    // Played: how long ago, then its time; never launched: how long it would take, when said.
    expect(rows.map((row) => row.querySelector('[data-said]')?.textContent?.trim())).toEqual([
      'Il y a 1 an · 48 h',
      'Jamais lancé · environ 22 h',
      'Jamais lancé',
    ]);
    expect(rows[2]?.querySelector('[data-name]')?.textContent?.trim()).toBe('Jeu sans nom');
    expect(rows[0]?.querySelector('a')?.getAttribute('href')).toBe(`/games/${sekiro}`);
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
    const sources = [...screen.querySelectorAll<HTMLButtonElement>('[data-cover-source]')];
    expect(
      sources.map((source) => [source.textContent.trim(), source.getAttribute('aria-pressed')]),
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
    // On its way, the cover waits, and keeps the focus the closed panel gave back to it. (The
    // request unanswered keeps the app busy: it is not waited for until it is answered.)
    await vi.waitFor(() => expect(cover.getAttribute('aria-disabled')).toBe('true'));
    expect(document.activeElement).toBe(cover);
    chosen.flush({ sources: ['steam', 'igdb'], chosen: 'igdb' });
    await harness.fixture.whenStable();
    expect(cover.hasAttribute('aria-disabled')).toBe(false);

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

  it('says a cover could not be chosen, until the covers are opened again', async () => {
    const { harness, server, screen } = await open(
      eldenRing({ covers: { sources: ['steam', 'igdb'], chosen: null } }),
    );

    const cover = screen.querySelector<HTMLButtonElement>('[data-cover-button]')!;
    cover.click();
    await harness.fixture.whenStable();
    screen.querySelector<HTMLButtonElement>('[data-cover-source="igdb"]')!.click();
    await server.refuse(`/games/${ID}/cover-choice`, 409, 'no_such_cover');
    await harness.fixture.whenStable();

    expect(text(screen, '[data-cover-refused]')).toBe(
      'La jaquette n’a pas pu être changée. Réessayez.',
    );

    cover.click();
    await harness.fixture.whenStable();
    expect(screen.querySelector('[data-cover-refused]')).toBeNull();
  });

  it('keeps the way to choose when the chosen cover cannot be shown', async () => {
    const { harness, screen } = await open(
      eldenRing({ covers: { sources: ['steam', 'igdb'], chosen: 'igdb' } }),
    );

    const cover = screen.querySelector<HTMLButtonElement>('[data-cover-button]')!;
    cover.querySelector('img')!.dispatchEvent(new Event('error'));
    await harness.fixture.whenStable();

    // The initial stands in the cover's place, inside the button, which still opens the covers.
    expect(cover.querySelector('[data-no-cover]')).not.toBeNull();
    expect(screen.querySelectorAll('[data-cover-button]')).toHaveLength(1);
    cover.click();
    await harness.fixture.whenStable();
    expect(screen.querySelector<HTMLElement>('[data-cover-panel]')!.hidden).toBe(false);
    expect(screen.querySelector('[data-cover-reset]')).not.toBeNull();
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

  it('says a game never launched in the hero alone, and draws no figure', async () => {
    const never = eldenRing({
      playtime_seconds: 0,
      recent_playtime_seconds: 0,
      last_played_at: null,
      rank: null,
      entries: [entry({ playtime_seconds: 0, recent_playtime_seconds: 0, last_played_at: null })],
    });
    const { screen } = await open(never, []);

    expect(text(screen, '[data-status]')).toBe('Jamais lancé');
    expect(screen.querySelector('[data-never]')).toBeNull();
    expect(screen.querySelector('[data-figures]')).toBeNull();
    expect(screen.querySelector('[data-calendar]')).toBeNull();
  });

  it('draws two figures for a game last played before this year', async () => {
    const { screen } = await open(
      eldenRing({
        recent_playtime_seconds: 0,
        last_played_at: `${YEAR - 1}-06-01T20:00:00Z`,
        entries: [
          entry({ recent_playtime_seconds: 0, last_played_at: `${YEAR - 1}-06-01T20:00:00Z` }),
        ],
      }),
    );

    expect(
      [...screen.querySelectorAll('[data-figure]')].map((tile) => tile.getAttribute('data-figure')),
    ).toEqual(['total', 'best']);
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
    // A notice changes none of its months: they are not asked again.
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
    await server.answer(LIBRARY_FIRST_PAGE, {
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
    await server.answer(LIBRARY_FIRST_PAGE, {
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
