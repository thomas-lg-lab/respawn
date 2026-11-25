import { Location } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { RouterTestingHarness } from '@angular/router/testing';
import {
  AWAITING_SYNC_INTERVAL,
  type LibraryGame,
  type LibraryHistory,
  type LibraryStats,
  type LibraryYears,
  type LinkedAccount,
  type SyncRun,
  TEST_LIBRARY_HISTORY as NO_HISTORY,
  TEST_LIBRARY_STATS as STATS,
  TEST_LIBRARY_YEARS as NO_YEARS,
  TEST_LINKED_ACCOUNT,
  TEST_SYNC_RUN,
} from '@respawn/data';
import { QueryClient } from '@tanstack/angular-query';
import { ACCOUNT, api, button, configureApp, type } from '@app/testing/app-testing';
import '../../layout/shell';
import './home';

type Server = ReturnType<typeof api>;

let runs = 0;

/** A reading that just succeeded: nothing to say about the sync. Each is a run of its own. */
function succeeded(at = new Date()): SyncRun {
  const id = `5f0c1f0a-3a0e-4f0b-8a53-${String(++runs).padStart(12, '0')}`;
  return { ...TEST_SYNC_RUN, id, started_at: at.toISOString(), finished_at: at.toISOString() };
}

function account(sync: Partial<LinkedAccount['sync']> = {}): LinkedAccount {
  const last = succeeded();
  return {
    ...TEST_LINKED_ACCOUNT,
    sync: { running: false, last, last_succeeded_at: last.finished_at, ...sync },
  };
}

/**
 * An account whose only reading so far ended this way, with no game; `null`: none ended,
 * and it was linked a moment ago unless the spec says when.
 */
function neverRead(last: Partial<SyncRun> | null, linkedAt = new Date()): LinkedAccount {
  return {
    ...account({ last: last && { ...succeeded(), ...last }, last_succeeded_at: null }),
    linked_at: linkedAt.toISOString(),
  };
}

const SYNC = `/linked-accounts/${TEST_LINKED_ACCOUNT.id}/sync`;
const GAMES = '/library?sort=name&offset=0&limit=500';
const HISTORY = '/library/history';
const YEARS = '/library/years';

let made = 0;

function game(name: string | null, values: Partial<LibraryGame> = {}): LibraryGame {
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

/** A small library that has been played, as the API gives it: by title. */
function played(): LibraryGame[] {
  return [
    game('Celeste'),
    game('Elden Ring', {
      playtime_seconds: 142 * 3600,
      recent_playtime_seconds: 6 * 3600,
      last_played_at: ago(1),
      entries: [
        {
          store_entry_id: '00000000-0000-4000-9000-000000000001',
          linked_account_id: TEST_LINKED_ACCOUNT.id,
          source: 'steam',
          external_id: '1245620',
          playtime_seconds: 142 * 3600,
          recent_playtime_seconds: 6 * 3600,
          last_played_at: ago(1),
          first_seen_at: '2026-11-18T10:05:00Z',
          as_of: '2026-11-20T04:02:05Z',
          missing_since: null,
        },
      ],
    }),
    game('Hades', { playtime_seconds: 100 * 3600, last_played_at: ago(400) }),
    game('Hollow Knight'),
    game('Portal 2', {
      playtime_seconds: 2 * 3600,
      recent_playtime_seconds: 45 * 60,
      last_played_at: ago(3),
    }),
    game('Tunic'),
    game('Unpacking'),
  ];
}

/**
 * The library as the home asks for it once an account was read: its figures, its games, its
 * history and its years.
 */
async function library(
  server: Server,
  stats: LibraryStats,
  games: LibraryGame[] = [],
  history: LibraryHistory = NO_HISTORY,
  years: LibraryYears = NO_YEARS,
): Promise<void> {
  await server.answer('/library/stats', stats);
  await server.answer(HISTORY, history);
  await server.answer(YEARS, years);
  await server.answer(GAMES, { total: games.length, offset: 0, limit: 500, games });
}

describe('Home', () => {
  /** Opens the home, signed in, with these accounts linked. */
  async function open(
    accounts: LinkedAccount[],
    path = '/',
    stats: LibraryStats | null = STATS,
    games: LibraryGame[] = [],
    history: LibraryHistory = NO_HISTORY,
    years: LibraryYears = NO_YEARS,
  ) {
    configureApp('fr');
    const harness = await RouterTestingHarness.create();
    const server = api();
    const arrived = harness.navigateByUrl(path);
    await server.answer('/me', ACCOUNT);
    await server.answer('/linked-accounts', { accounts });
    const read = accounts.some((each) => each.sync.last_succeeded_at);
    if (read && stats) await library(server, stats, games, history, years);
    await arrived;
    await harness.fixture.whenStable();
    return { harness, server, screen: harness.fixture.nativeElement as HTMLElement };
  }

  const title = (screen: HTMLElement) => screen.querySelector('h1')?.textContent.trim();
  const text = (screen: HTMLElement, selector: string) =>
    screen.querySelector(selector)?.textContent.replace(/\s+/g, ' ').trim();
  const settled = async (
    harness: RouterTestingHarness,
    server: Server,
    accounts: LinkedAccount[],
  ) => {
    await server.answer('/linked-accounts', { accounts });
    await harness.fixture.whenStable();
  };

  beforeEach(() => localStorage.clear());
  afterEach(() => api().http.verify());

  it('leads to the Steam link when no account is linked', async () => {
    const { screen } = await open([]);

    expect(title(screen)).toBe('Aucun compte de jeu lié');
    expect(screen.querySelector('[data-link-steam]')?.getAttribute('href')).toBe(
      '/api/v1/linked-accounts/steam/link?return_to=%2F',
    );
  });

  it('says that the account was linked, once, and reads its library at once', async () => {
    configureApp('fr');
    const harness = await RouterTestingHarness.create();
    const server = api();
    const arrived = harness.navigateByUrl('/?steam_link=linked');
    await server.answer('/me', ACCOUNT);
    await server.home([neverRead(null)]);
    await arrived;
    const screen = harness.fixture.nativeElement as HTMLElement;

    // The first reading is asked without waiting for the schedule.
    const asked = await server.request(SYNC);
    expect(text(screen, '[data-steam-link="linked"]')).toBe('Votre compte Steam est lié.');
    expect(title(screen)).toBe('Première synchronisation');
    // The word is off the address: a reload does not say it again.
    expect(TestBed.inject(Location).path()).not.toContain('steam_link');

    asked.flush(succeeded());
    await server.home([account()]);
    await library(server, STATS);
    await harness.fixture.whenStable();
    expect(title(screen)).toBe('Accueil');
  });

  it('leaves to the schedule an account that was linked before this visit', async () => {
    const { screen } = await open([neverRead(null)]);

    expect(title(screen)).toBe('Première synchronisation');
    // `verify` fails on a sync nobody answered: none was asked.
  });

  it('says nothing of a first reading the schedule started before it was asked', async () => {
    configureApp('fr');
    const harness = await RouterTestingHarness.create();
    const server = api();
    const arrived = harness.navigateByUrl('/?steam_link=linked');
    await server.answer('/me', ACCOUNT);
    await server.home([neverRead(null)]);
    await arrived;
    const screen = harness.fixture.nativeElement as HTMLElement;

    await server.refuse(SYNC, 409, 'sync_in_progress');
    await settled(harness, server, [neverRead(null)]);

    expect(screen.querySelector('[data-sync-refused]')).toBeNull();
    expect(title(screen)).toBe('Première synchronisation');
  });

  it('stops waiting for a first reading that never came, and offers to ask for it', async () => {
    const longAgo = new Date(Date.now() - 60 * 60 * 1000);
    const { harness, server, screen } = await open([neverRead(null, longAgo)]);

    expect(text(screen, 'rs-first-sync p.text-muted')).toContain('n’a pas abouti');
    expect(screen.querySelector('rs-skeleton')).toBeNull();

    button(screen, 'Synchroniser maintenant').click();
    const asked = await server.request(SYNC);
    asked.flush(succeeded());
    await server.home([account()]);
    await library(server, STATS);
    await harness.fixture.whenStable();
    expect(title(screen)).toBe('Accueil');
  });

  it('asks no reading when the link came back for an account already read', async () => {
    const { screen } = await open([account()], '/?steam_link=linked');

    expect(title(screen)).toBe('Accueil');
  });

  it('says why a link did not go through', async () => {
    const { screen } = await open([], '/?steam_link=linked_elsewhere');

    expect(text(screen, '[role="alert"]')).toBe('Ce compte Steam est déjà lié à un autre compte.');
    expect(title(screen)).toBe('Aucun compte de jeu lié');
  });

  it('shows the library as soon as the first reading ended', async () => {
    // Only the interval the accounts are asked again on is the test's to move: the rest of
    // the app keeps the real clock.
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    try {
      const { harness, server, screen } = await open([neverRead(null)]);
      expect(title(screen)).toBe('Première synchronisation');

      // The accounts are asked again while the reading is awaited.
      vi.advanceTimersByTime(AWAITING_SYNC_INTERVAL);
      await server.home([account()]);
      await library(server, STATS);
      await harness.fixture.whenStable();

      expect(title(screen)).toBe('Accueil');
    } finally {
      vi.useRealTimers();
    }
  });

  it('says why the first reading failed, and tries again on request', async () => {
    const failed = neverRead({ outcome: 'failed', error: 'refused' });
    const { harness, server, screen } = await open([failed]);

    expect(title(screen)).toBe('La synchronisation avec Steam a échoué');
    expect(text(screen, '[data-sync-reason]')).toContain(
      'Steam a refusé la clé d’API de l’instance',
    );

    button(screen, 'Réessayer').click();
    const asked = await server.request(SYNC);
    // While it reads, the screen waits like for a first reading.
    await vi.waitFor(() => expect(title(screen)).toBe('Première synchronisation'));

    asked.flush(succeeded());
    // The accounts are asked again, and the run that ended makes the figures stale.
    await server.home([account()]);
    await library(server, STATS);
    await harness.fixture.whenStable();
    expect(title(screen)).toBe('Accueil');
  });

  it('tells a key the user gave from the instance’s when Steam refuses it', async () => {
    const { screen } = await open([
      neverRead({ outcome: 'failed', error: 'refused', own_key: true }),
    ]);

    expect(text(screen, '[data-sync-reason]')).toContain('Steam a refusé votre clé d’API.');
  });

  it('gives the steps to open a profile when Steam showed no game', async () => {
    const { screen } = await open([neverRead({ outcome: 'nothing_visible' })]);

    expect(title(screen)).toBe('Steam ne nous montre aucun jeu');
    expect(screen.querySelectorAll('ol li')).toHaveLength(3);
    expect(() => button(screen, 'Synchroniser à nouveau')).not.toThrow();
  });

  it('shows the library in figures, each saying what it covers', async () => {
    const { screen } = await open([account()]);

    expect(title(screen)).toBe('Accueil');
    expect(text(screen, '[data-figure="total"] .figure')).toBe('612 h');
    expect(text(screen, '[data-figure="total"] .note')).toBe('sur 861 jeux');
    expect(text(screen, '[data-figure="recent"] .figure')).toBe('14 h');
    expect(text(screen, '[data-figure="recent"] .note')).toBe('sur 3 jeux');
    expect(text(screen, '[data-figure="never-launched"] .figure')).toBe('444');
    expect(text(screen, '[data-last-sync]')).toContain('Dernière synchronisation réussie');
    expect(screen.querySelector('[data-sync-notice]')).toBeNull();
  });

  it('says a figure Steam did not give is unknown, never zero', async () => {
    const { screen } = await open([account()], '/', {
      ...STATS,
      recent_playtime_seconds: null,
      recently_played: 0,
    });

    expect(text(screen, '[data-figure="recent"] .figure')).toBe('—');
    expect(text(screen, '[data-figure="recent"] .note')).toBe('Steam ne le dit pas pour ce profil');
    expect(text(screen, '[data-figure="never-launched"] .figure')).toBe('444');
  });

  it('counts games as the language does: none is singular in French', async () => {
    const { screen } = await open([account()], '/', {
      ...STATS,
      recent_playtime_seconds: 0,
      recently_played: 0,
    });

    expect(text(screen, '[data-figure="recent"] .note')).toBe('sur 0 jeu');
  });

  it('keeps a heading on the screens that say what went wrong', async () => {
    const { screen } = await open([neverRead({ outcome: 'failed', error: 'refused' })]);

    expect(screen.querySelector('h1')?.hasAttribute('role')).toBe(false);
    expect(text(screen, '[role="alert"]')).toContain('Steam a refusé');
  });

  it('does not count every game as never launched when no playtime was given', async () => {
    const { screen } = await open([account()], '/', {
      ...STATS,
      playtime_seconds: null,
      launched: 0,
      never_launched: STATS.games,
    });

    expect(text(screen, '[data-figure="never-launched"] .figure')).toBe('—');
    expect(screen.querySelector('[data-figure="never-launched"] rs-segmented-bar')).toBeNull();
  });

  it('asks the figures again on request when they could not be read', async () => {
    configureApp('fr');
    const harness = await RouterTestingHarness.create();
    const server = api();
    const arrived = harness.navigateByUrl('/');
    await server.answer('/me', ACCOUNT);
    await server.home([account()]);
    await server.refuse('/library/stats', 503, 'database_unavailable');
    await server.answer(HISTORY, NO_HISTORY);
    await server.answer(YEARS, NO_YEARS);
    await server.answer(GAMES, { total: 0, offset: 0, limit: 500, games: [] });
    await arrived;
    await harness.fixture.whenStable();
    const screen = harness.fixture.nativeElement as HTMLElement;

    screen.querySelector<HTMLButtonElement>('[data-figures-retry]')!.click();
    await server.answer('/library/stats', STATS);
    await harness.fixture.whenStable();

    expect(text(screen, '[data-figure="total"] .figure')).toBe('612 h');
  });

  it('keeps the games on screen when the last reading failed, and says so', async () => {
    const last = { ...succeeded(), outcome: 'failed', error: 'unavailable' };
    const { screen } = await open([account({ last })]);

    expect(text(screen, '[data-sync-notice="failed"]')).toContain('Steam ne répond pas.');
    expect(text(screen, '[data-figure="total"] .figure')).toBe('612 h');
  });

  it('says that a sync is running, and offers no second one', async () => {
    const { screen } = await open([account({ running: true })]);

    expect(text(screen, '[data-sync-notice="running"]')).toContain('Synchronisation avec Steam');
    expect(screen.querySelector<HTMLButtonElement>('[data-sync-now]')?.disabled).toBe(true);
  });

  it('points out a last success that is old, until it is put away', async () => {
    const old = new Date(Date.now() - 3 * 24 * 60 * 60 * 1000);
    const { harness, screen } = await open([
      account({ last: succeeded(old), last_succeeded_at: old.toISOString() }),
    ]);

    expect(text(screen, '[data-sync-notice="stale"]')).toContain(
      'La dernière synchronisation réussie date du',
    );

    screen.querySelector<HTMLButtonElement>('[data-sync-dismiss]')!.click();
    await harness.fixture.whenStable();
    expect(screen.querySelector('[data-sync-notice]')).toBeNull();
  });

  it('syncs now, then shows the figures as they are after it', async () => {
    const { harness, server, screen } = await open([account()]);

    screen.querySelector<HTMLButtonElement>('[data-sync-now]')!.click();
    const asked = await server.request(SYNC);
    await vi.waitFor(() => expect(text(screen, '[data-sync-notice="running"]')).toBeTruthy());

    asked.flush(succeeded());
    // The accounts are asked again, and the run that ended makes the figures stale.
    await server.home([account()]);
    await library(server, { ...STATS, playtime_seconds: 613 * 3600 });
    await harness.fixture.whenStable();

    expect(text(screen, '[data-figure="total"] .figure')).toBe('613 h');
    expect(screen.querySelector('[data-sync-notice]')).toBeNull();
  });

  it('says that a sync was refused because one is already running', async () => {
    const linked = account();
    const { harness, server, screen } = await open([linked]);

    screen.querySelector<HTMLButtonElement>('[data-sync-now]')!.click();
    await server.refuse(SYNC, 409, 'sync_in_progress');
    // No run ended: the accounts are asked again, the figures are not.
    await settled(harness, server, [linked]);

    expect(text(screen, '[data-sync-refused]')).toBe('Une synchronisation est déjà en cours.');
  });

  it('stops saying a sync was refused once a sync ended, or when it is put away', async () => {
    const linked = account();
    const { harness, server, screen } = await open([linked]);
    const refuse = async () => {
      screen.querySelector<HTMLButtonElement>('[data-sync-now]')!.click();
      await server.refuse(SYNC, 429, 'too_many_requests');
      await settled(harness, server, [linked]);
    };

    await refuse();
    screen.querySelector<HTMLButtonElement>('[data-refusal-dismiss]')!.click();
    await harness.fixture.whenStable();
    expect(screen.querySelector('[data-sync-refused]')).toBeNull();

    await refuse();
    expect(screen.querySelector('[data-sync-refused]')).not.toBeNull();
    // Another reading ended since (the schedule's): what was refused is no longer the point.
    void TestBed.inject(QueryClient).invalidateQueries({ queryKey: ['linked-accounts'] });
    await server.home([account()]);
    await library(server, STATS);
    await harness.fixture.whenStable();
    expect(screen.querySelector('[data-sync-refused]')).toBeNull();
  });

  it('says that a sync was asked too soon after the last one', async () => {
    const linked = account();
    const { harness, server, screen } = await open([linked]);

    screen.querySelector<HTMLButtonElement>('[data-sync-now]')!.click();
    await server.refuse(SYNC, 429, 'too_many_requests');
    await settled(harness, server, [linked]);

    expect(text(screen, '[data-sync-refused]')).toBe(
      'Une synchronisation vient d’être demandée. Réessayez dans une minute.',
    );
  });

  it('says that the API could not tell which accounts are linked, and asks again', async () => {
    configureApp('fr');
    const harness = await RouterTestingHarness.create();
    const server = api();
    const arrived = harness.navigateByUrl('/');
    await server.answer('/me', ACCOUNT);
    await server.refuse('/linked-accounts', 503, 'database_unavailable');
    await arrived;
    await harness.fixture.whenStable();
    const screen = harness.fixture.nativeElement as HTMLElement;

    expect(text(screen, '[role="alert"]')).toBe(
      'La base de données ne répond pas. Réessayez dans un instant.',
    );

    button(screen, 'Réessayer').click();
    await settled(harness, server, []);
    expect(title(screen)).toBe('Aucun compte de jeu lié');
  });

  describe('the blocks made of the games', () => {
    const names = (screen: HTMLElement, block: string) =>
      [...screen.querySelectorAll(`[data-${block}] [data-name]`)].map((each) =>
        each.textContent.trim(),
      );

    // A draw is told what chance gives: never left so for the tests that follow.
    afterEach(() => vi.restoreAllMocks());

    it('puts forward the game played last, with what Steam says of it', async () => {
      const { screen } = await open([account()], '/', STATS, played());

      expect(text(screen, '[data-resume-label]')).toBe('Reprendre · dernière session hier');
      expect(text(screen, '[data-resume-name]')).toBe('Elden Ring');
      expect(text(screen, '[data-resume-time]')).toBe(
        '142 h au total · 6 h ces deux dernières semaines',
      );
    });

    it('launches the game in Steam, when a copy of it is there', async () => {
      const { screen } = await open([account()], '/', STATS, played());

      const launch = screen.querySelector<HTMLAnchorElement>('[data-launch]')!;
      expect(launch.textContent.trim()).toBe('Lancer dans Steam');
      expect(launch.getAttribute('href')).toBe('steam://run/1245620');
    });

    it('offers no launch for a game no Steam copy of which is known', async () => {
      const games = played().map((each) => ({ ...each, entries: [] }));
      const { screen } = await open([account()], '/', STATS, games);

      expect(text(screen, '[data-resume-name]')).toBe('Elden Ring');
      expect(screen.querySelector('[data-launch]')).toBeNull();
    });

    it('lists the other games of the last two weeks beside it', async () => {
      const { screen } = await open([account()], '/', STATS, played());

      expect(names(screen, 'recently')).toEqual(['Portal 2']);
      expect(text(screen, '[data-recently] [data-played]')).toBe(
        '45 min ces 2 semaines · 2 h au total',
      );
    });

    it('falls back on the latest sessions when Steam names no game of the two weeks', async () => {
      const games = played().map((each) => ({ ...each, recent_playtime_seconds: null }));
      const { screen } = await open([account()], '/', STATS, games);

      expect(text(screen, '[data-resume-time]')).toBe('142 h au total');
      expect(names(screen, 'recently')).toEqual(['Portal 2', 'Hades']);
      expect(text(screen, '[data-recently] [data-played]')).toBe('Il y a 3 jours · 2 h au total');
    });

    it('ranks the most played games and sums up the others', async () => {
      const more = Array.from({ length: 6 }, (_, index) =>
        game(`Jeu ${index}`, { playtime_seconds: (index + 1) * 60 }),
      );
      const { screen } = await open([account()], '/', STATS, [...played(), ...more]);

      expect(names(screen, 'ranking')).toEqual([
        'Elden Ring',
        'Hades',
        'Portal 2',
        'Jeu 5',
        'Jeu 4',
        'Jeu 3',
      ]);
      expect(text(screen, '[data-ranking-total]')).toBe('244 h au total');
      expect(text(screen, '[data-ranking-others]')).toBe('Les 3 autres jeux lancés : 6 min.');
    });

    it('draws the time map of the most played games, coloured by their last session', async () => {
      const { screen } = await open([account()], '/', STATS, played());

      const tiles = [...screen.querySelectorAll('[data-time-map] [data-game]')];
      // Elden Ring and Portal 2 were played these two weeks, Hades over a year ago.
      expect(tiles.map((tile) => tile.getAttribute('data-recency')).sort()).toEqual([
        '1',
        '1',
        '4',
      ]);
      expect(text(screen, '[data-time-map] h2')).toBe('La carte du temps');
      expect(screen.querySelector('[data-time-link]')?.getAttribute('href')).toBe('/time');
      expect(text(screen, '[data-time-map] p')).toBe(
        'Les 3 jeux les plus joués : une case à la mesure du temps de jeu, d’autant plus chaude que la dernière session est récente.',
      );
    });

    it('cuts the time map for the shape it is drawn in', async () => {
      let resized: ResizeObserverCallback = () => undefined;
      vi.stubGlobal(
        'ResizeObserver',
        class {
          constructor(callback: ResizeObserverCallback) {
            resized = callback;
          }
          observe() {}
          disconnect() {}
        },
      );
      try {
        const games = [
          game('Hades', { playtime_seconds: 3600 }),
          game('Tunic', { playtime_seconds: 3600 }),
        ];
        const { harness, screen } = await open([account()], '/', STATS, games);
        const first = () => screen.querySelector<HTMLElement>('[data-time-map] [data-game]');

        // Wider than tall until it is measured: the two halves side by side.
        expect(first()?.style.width).toBe('50%');
        expect(first()?.style.height).toBe('100%');

        const drawn = { contentRect: { width: 100, height: 400 } } as ResizeObserverEntry;
        resized([drawn], {} as ResizeObserver);
        await harness.fixture.whenStable();

        // Taller than wide: one above the other.
        expect(first()?.style.width).toBe('100%');
        expect(first()?.style.height).toBe('50%');
      } finally {
        vi.unstubAllGlobals();
      }
    });

    it('draws no time map of one launched game alone', async () => {
      const games = [game('Celeste'), game('Hades', { playtime_seconds: 3600 })];
      const { screen } = await open([account()], '/', STATS, games);

      expect(screen.querySelector('[data-ranking]')).not.toBeNull();
      expect(screen.querySelector('[data-time-map]')).toBeNull();
    });

    it('draws three games never launched, and others on request', async () => {
      const random = vi.spyOn(Math, 'random').mockReturnValue(0);
      const { harness, screen } = await open([account()], '/', STATS, played());
      // Four were never launched: Celeste, Hollow Knight, Tunic, Unpacking.
      expect(names(screen, 'picks')).toEqual(['Celeste', 'Hollow Knight', 'Tunic']);

      random.mockReturnValue(0.99);
      button(screen, 'Autre tirage').click();
      await harness.fixture.whenStable();

      expect(names(screen, 'picks')).toEqual(['Unpacking', 'Tunic', 'Hollow Knight']);
    });

    it('keeps the draw when the games are read again', async () => {
      vi.spyOn(Math, 'random').mockReturnValue(0);
      const games = played();
      const { harness, server, screen } = await open([account()], '/', STATS, games);
      expect(names(screen, 'picks')).toEqual(['Celeste', 'Hollow Knight', 'Tunic']);

      vi.spyOn(Math, 'random').mockReturnValue(0.99);
      screen.querySelector<HTMLButtonElement>('[data-sync-now]')!.click();
      const asked = await server.request(SYNC);
      asked.flush(succeeded());
      await server.home([account()]);
      // The same library, read anew: every game is another object.
      await library(
        server,
        STATS,
        games.map((each) => ({ ...each })),
      );
      await harness.fixture.whenStable();

      expect(names(screen, 'picks')).toEqual(['Celeste', 'Hollow Knight', 'Tunic']);
    });

    it('draws the letter behind the title whole, when it is an emoji', async () => {
      const games = played().map((each) =>
        each.name === 'Elden Ring' ? { ...each, name: '🎮 Arcade' } : each,
      );
      const { screen } = await open([account()], '/', STATS, games);

      expect(text(screen, '[data-resume] .letter')).toBe('🎮');
    });

    it('offers no other draw when every game never launched is shown', async () => {
      const { screen } = await open([account()], '/', STATS, played().slice(0, 4));

      expect(names(screen, 'picks').sort()).toEqual(['Celeste', 'Hollow Knight']);
      expect(screen.querySelector('[data-reroll]')).toBeNull();
    });

    it('shows no block a library that was never played has nothing to put in', async () => {
      const { screen } = await open([account()], '/', STATS, [game('Celeste'), game(null)]);

      expect(screen.querySelector('[data-resume]')).toBeNull();
      expect(screen.querySelector('[data-recently]')).toBeNull();
      expect(screen.querySelector('[data-ranking]')).toBeNull();
      expect(screen.querySelector('[data-time-map]')).toBeNull();
      expect(names(screen, 'picks').sort()).toEqual(['Celeste', 'Jeu sans nom']);
    });

    it('leads to the Library with the title that was searched', async () => {
      const { harness, server, screen } = await open([account()], '/', STATS, played());

      type(screen.querySelector<HTMLInputElement>('[data-search] input')!, ' portal ');
      button(screen, 'Chercher').click();
      await harness.fixture.whenStable();

      expect(title(screen)).toBe('Bibliothèque');
      expect(names(screen, 'game')).toEqual(['Portal 2']);
      // The games were already read for the home: they are not asked again.
      server.http.expectNone(`/api/v1${GAMES}`);
      // The title is in the field, and off the address.
      expect(screen.querySelector<HTMLInputElement>('rs-library-toolbar input')?.value).toBe(
        'portal',
      );
      expect(TestBed.inject(Location).path()).toBe('/library');
    });

    it('says that the games could not be read, and asks again on request', async () => {
      configureApp('fr');
      const harness = await RouterTestingHarness.create();
      const server = api();
      const arrived = harness.navigateByUrl('/');
      await server.answer('/me', ACCOUNT);
      await server.home([account()]);
      await server.answer('/library/stats', STATS);
      await server.answer(HISTORY, NO_HISTORY);
      await server.answer(YEARS, NO_YEARS);
      await server.refuse(GAMES, 503, 'database_unavailable');
      await arrived;
      await harness.fixture.whenStable();
      const screen = harness.fixture.nativeElement as HTMLElement;

      expect(text(screen, '[data-games-refused]')).toBe(
        'La base de données ne répond pas. Réessayez dans un instant.',
      );
      expect(text(screen, '[data-figure="total"] .figure')).toBe('612 h');

      screen.querySelector<HTMLButtonElement>('[data-games-retry]')!.click();
      await server.answer(GAMES, { total: 7, offset: 0, limit: 500, games: played() });
      await harness.fixture.whenStable();

      expect(text(screen, '[data-resume-name]')).toBe('Elden Ring');
    });

    it('says the blocks in English too', async () => {
      configureApp('en');
      const harness = await RouterTestingHarness.create();
      const server = api();
      const arrived = harness.navigateByUrl('/');
      await server.answer('/me', ACCOUNT);
      await server.home([account()]);
      await library(server, STATS, played());
      await arrived;
      await harness.fixture.whenStable();
      const screen = harness.fixture.nativeElement as HTMLElement;

      expect(text(screen, '[data-resume-label]')).toBe('Resume · last session yesterday');
      expect(text(screen, '[data-resume-time]')).toBe('142 h in all · 6 h over the last two weeks');
      expect(text(screen, '[data-recently] h2')).toBe('Played recently');
      expect(text(screen, '[data-ranking] h2')).toBe('Where the time went');
      expect(text(screen, '[data-time-map] h2')).toBe('The time map');
      expect(text(screen, '[data-picks] h2')).toBe('Never launched, at random');
    });
  });

  describe('the blocks made of the history', () => {
    const all = (screen: HTMLElement, selector: string) =>
      [...screen.querySelectorAll(selector)].map((each) =>
        each.textContent.replace(/\s+/g, ' ').trim(),
      );

    /** The years as the API counts them from the two days of readings below: 2026, about 4 h. */
    const synced = (): LibraryYears => ({
      years: [
        {
          year: 2026,
          seconds: 3 * 3600 + 55 * 60,
          source: 'readings',
          first_day: '2026-11-18',
          last_day: '2026-11-28',
          whole: false,
        },
      ],
    });

    /** A year of Steam's Year in Review: the whole of it. */
    const reviewed = (year: number, seconds: number): LibraryYears['years'][number] => ({
      year,
      seconds,
      source: 'review',
      first_day: `${year}-01-01`,
      last_day: `${year}-12-31`,
      whole: true,
    });

    /** Two days of readings over the small library, and a game that left it since. */
    function readings(games: LibraryGame[]): LibraryHistory {
      const id = (name: string) => games.find((each) => each.name === name)!.game_id;
      const [elden, portal] = [id('Elden Ring'), id('Portal 2')];
      return {
        tracking_since: '2026-11-18T10:05:00Z',
        days: [
          {
            date: '2026-11-20',
            games: [
              { game_id: elden, seconds: 2 * 3600 },
              { game_id: portal, seconds: 45 * 60 },
              { game_id: '00000000-0000-4000-8000-ffffffffffff', seconds: 10 * 60 },
            ],
          },
          { date: '2026-11-21', games: [{ game_id: elden, seconds: 3600 }] },
        ],
      };
    }

    it('shows the time played per year, and says since when it is counted', async () => {
      const games = played();
      const { screen } = await open([account()], '/', STATS, games, readings(games), synced());

      expect(text(screen, '[data-year="2026"] .said')).toBe('4 h');
      expect(text(screen, '[data-year="2026"] .label')).toBe('2026');
      // The year of the first sync is not whole.
      expect(screen.querySelector('[data-year="2026"]')?.hasAttribute('data-open')).toBe(true);
      expect(text(screen, '[data-years-since]')).toBe(
        '2026 : compté jour par jour à chaque synchronisation, depuis le 18 nov. 2026.',
      );
      expect(text(screen, '[data-years-open]')).toBe('Une année en pointillés n’est pas entière.');
      expect(screen.querySelector('[data-years-reviewed]')).toBeNull();
    });

    it('draws the years of Steam’s Year in Review whole, before those of the syncs', async () => {
      const games = played();
      const { screen } = await open([account()], '/', STATS, games, readings(games), {
        years: [reviewed(2024, 300 * 3600), reviewed(2025, 0), ...synced().years],
      });

      expect(all(screen, '[data-years] .label')).toEqual(['2024', '2025', '2026']);
      expect(text(screen, '[data-year="2024"] .said')).toBe('300 h');
      expect(screen.querySelector('[data-year="2024"]')?.hasAttribute('data-open')).toBe(false);
      expect(screen.querySelector('[data-year="2025"]')?.hasAttribute('data-open')).toBe(false);
      expect(text(screen, '[data-years-reviewed]')).toBe(
        '2024–2025 : d’après la rétrospective annuelle de Steam, mois par mois.',
      );
      expect(text(screen, '[data-years-since]')).toBe(
        '2026 : compté jour par jour à chaque synchronisation, depuis le 18 nov. 2026.',
      );
    });

    it('names in each caption only the years it speaks of', async () => {
      const games = played();
      // 2023 has no review for every account and comes before the first sync: it is not drawn.
      // 2027 is reviewed, between two years of the syncs.
      const { screen } = await open([account()], '/', STATS, games, readings(games), {
        years: [
          reviewed(2022, 3600),
          reviewed(2024, 7200),
          ...synced().years,
          reviewed(2027, 3600),
          { ...synced().years[0]!, year: 2028, first_day: '2028-01-01', last_day: '2028-03-01' },
        ],
      });

      expect(all(screen, '[data-years] .label')).toEqual(['2022', '2024', '2026', '2027', '2028']);
      expect(text(screen, '[data-years-reviewed]')).toBe(
        '2022, 2024, 2027 : d’après la rétrospective annuelle de Steam, mois par mois.',
      );
      expect(text(screen, '[data-years-since]')).toBe(
        '2026, 2028 : compté jour par jour à chaque synchronisation, depuis le 18 nov. 2026.',
      );
    });

    it('says nothing of the syncs or of a dashed year when every year is reviewed', async () => {
      const games = played();
      const { screen } = await open([account()], '/', STATS, games, readings(games), {
        years: [reviewed(2025, 3600)],
      });

      expect(text(screen, '[data-years-reviewed]')).toBe(
        '2025 : d’après la rétrospective annuelle de Steam, mois par mois.',
      );
      expect(screen.querySelector('[data-years-since]')).toBeNull();
      expect(screen.querySelector('[data-years-open]')).toBeNull();
    });

    it('says that the years could not be read, keeps the activity, and asks again', async () => {
      configureApp('fr');
      const harness = await RouterTestingHarness.create();
      const server = api();
      const arrived = harness.navigateByUrl('/');
      await server.answer('/me', ACCOUNT);
      await server.home([account()]);
      const games = played();
      await server.answer('/library/stats', STATS);
      await server.answer(GAMES, { total: games.length, offset: 0, limit: 500, games });
      await server.answer(HISTORY, readings(games));
      await server.refuse(YEARS, 503, 'database_unavailable');
      await arrived;
      await harness.fixture.whenStable();
      const screen = harness.fixture.nativeElement as HTMLElement;

      expect(text(screen, '[data-history-refused]')).toBe(
        'La base de données ne répond pas. Réessayez dans un instant.',
      );
      expect(screen.querySelector('[data-years]')).toBeNull();
      expect(all(screen, '[data-activity] h3')).toHaveLength(2);

      screen.querySelector<HTMLButtonElement>('[data-history-retry]')!.click();
      await server.answer(YEARS, synced());
      await harness.fixture.whenStable();

      expect(screen.querySelector('[data-history-refused]')).toBeNull();
      expect(text(screen, '[data-year="2026"] .said')).toBe('4 h');
    });

    it('lists the latest days played, the latest first, each game with its time', async () => {
      const games = played();
      const { screen } = await open([account()], '/', STATS, games, readings(games));

      expect(all(screen, '[data-activity] h3')).toEqual([
        'Samedi 21 novembre',
        'Vendredi 20 novembre',
      ]);
      expect(all(screen, '[data-day="2026-11-21"] [data-name]')).toEqual(['Elden Ring']);
      expect(all(screen, '[data-day="2026-11-21"] [data-playtime]')).toEqual(['1 h']);
      expect(all(screen, '[data-day="2026-11-21"] [data-total]')).toEqual(['142 h au total']);
      expect(all(screen, '[data-day="2026-11-20"] [data-name]')).toEqual([
        'Elden Ring',
        'Portal 2',
        'Un jeu qui n’est plus dans votre bibliothèque',
      ]);
      expect(all(screen, '[data-day="2026-11-20"] [data-playtime]')).toEqual([
        '2 h',
        '45 min',
        '10 min',
      ]);
      expect(screen.querySelector('[data-activity-empty]')).toBeNull();
    });

    it('says when the tracking began while no day was played, and draws no year', async () => {
      const { screen } = await open([account()], '/', STATS, played());

      expect(text(screen, '[data-activity-since]')).toBe('Le suivi a commencé le 18 nov. 2026');
      expect(text(screen, '[data-activity-empty]')).toContain(
        'Steam ne donne pas le détail des sessions passées. respawn relève vos temps de jeu une fois par jour : ce que vous jouez s’affichera ici après la prochaine lecture.',
      );
      expect(screen.querySelector('[data-activity-note]')).toBeNull();
      expect(screen.querySelector('[data-years]')).toBeNull();
    });

    it('says that the history could not be read, keeps the other blocks, and asks again', async () => {
      configureApp('fr');
      const harness = await RouterTestingHarness.create();
      const server = api();
      const arrived = harness.navigateByUrl('/');
      await server.answer('/me', ACCOUNT);
      await server.home([account()]);
      const games = played();
      await server.answer('/library/stats', STATS);
      await server.answer(GAMES, { total: games.length, offset: 0, limit: 500, games });
      await server.refuse(HISTORY, 503, 'database_unavailable');
      await server.answer(YEARS, synced());
      await arrived;
      await harness.fixture.whenStable();
      const screen = harness.fixture.nativeElement as HTMLElement;

      expect(text(screen, '[data-history-refused]')).toBe(
        'La base de données ne répond pas. Réessayez dans un instant.',
      );
      expect(text(screen, '[data-ranking] h2')).toBe('Où est passé le temps');
      expect(screen.querySelector('[data-activity]')).toBeNull();
      // The years do not depend on the history: they are drawn all the same.
      expect(text(screen, '[data-year="2026"] .said')).toBe('4 h');

      screen.querySelector<HTMLButtonElement>('[data-history-retry]')!.click();
      await server.answer(HISTORY, readings(games));
      await harness.fixture.whenStable();

      expect(screen.querySelector('[data-history-refused]')).toBeNull();
      expect(all(screen, '[data-activity] h3')).toHaveLength(2);
    });

    it('asks both again when the history and the years could not be read', async () => {
      configureApp('fr');
      const harness = await RouterTestingHarness.create();
      const server = api();
      const arrived = harness.navigateByUrl('/');
      await server.answer('/me', ACCOUNT);
      await server.home([account()]);
      const games = played();
      await server.answer('/library/stats', STATS);
      await server.answer(GAMES, { total: games.length, offset: 0, limit: 500, games });
      await server.refuse(HISTORY, 503, 'database_unavailable');
      await server.refuse(YEARS, 503, 'database_unavailable');
      await arrived;
      await harness.fixture.whenStable();
      const screen = harness.fixture.nativeElement as HTMLElement;

      expect(screen.querySelectorAll('[data-history-refused]')).toHaveLength(1);
      expect(screen.querySelector('[data-years]')).toBeNull();
      expect(screen.querySelector('[data-activity]')).toBeNull();

      screen.querySelector<HTMLButtonElement>('[data-history-retry]')!.click();
      await server.answer(HISTORY, readings(games));
      await server.answer(YEARS, synced());
      await harness.fixture.whenStable();

      expect(screen.querySelector('[data-history-refused]')).toBeNull();
      expect(text(screen, '[data-year="2026"] .said')).toBe('4 h');
      expect(all(screen, '[data-activity] h3')).toHaveLength(2);
    });

    it('reads the history again once a sync ended', async () => {
      const games = played();
      const { harness, server, screen } = await open([account()], '/', STATS, games);
      expect(screen.querySelector('[data-activity-empty]')).not.toBeNull();

      screen.querySelector<HTMLButtonElement>('[data-sync-now]')!.click();
      (await server.request(SYNC)).flush(succeeded());
      await server.home([account()]);
      await library(server, STATS, games, readings(games), synced());
      await harness.fixture.whenStable();

      expect(all(screen, '[data-activity] h3')).toHaveLength(2);
      expect(text(screen, '[data-year="2026"] .said')).toBe('4 h');
    });

    it('shows the blocks made of the games while the history is awaited', async () => {
      configureApp('fr');
      const harness = await RouterTestingHarness.create();
      const server = api();
      const arrived = harness.navigateByUrl('/');
      await server.answer('/me', ACCOUNT);
      await server.home([account()]);
      const games = played();
      await server.answer('/library/stats', STATS);
      await server.answer(GAMES, { total: games.length, offset: 0, limit: 500, games });
      const history = await server.request(HISTORY);
      const years = await server.request(YEARS);
      await arrived;
      const screen = harness.fixture.nativeElement as HTMLElement;

      // Not `whenStable`: the history is still awaited, on purpose.
      await vi.waitFor(() =>
        expect(text(screen, '[data-ranking] h2')).toBe('Où est passé le temps'),
      );
      expect(screen.querySelector('[data-picks]')).not.toBeNull();
      expect(screen.querySelector('[data-history-awaited]')).not.toBeNull();
      expect(screen.querySelector('[data-years-awaited]')).not.toBeNull();
      expect(screen.querySelector('[data-activity]')).toBeNull();

      history.flush(readings(games));
      years.flush(synced());
      await harness.fixture.whenStable();

      expect(screen.querySelector('[data-history-awaited]')).toBeNull();
      expect(screen.querySelector('[data-years-awaited]')).toBeNull();
      expect(screen.querySelector('[data-years]')).not.toBeNull();
      expect(all(screen, '[data-activity] h3')).toHaveLength(2);
    });

    it('does not say gone a game the history has before the games asked again do', async () => {
      const games = played();
      const { harness, server, screen } = await open([account()], '/', STATS, games);
      const bought = game('Outer Wilds', { playtime_seconds: 3600 });
      const told: LibraryHistory = {
        tracking_since: '2026-11-18T10:05:00Z',
        days: [{ date: '2026-11-21', games: [{ game_id: bought.game_id, seconds: 3600 }] }],
      };

      screen.querySelector<HTMLButtonElement>('[data-sync-now]')!.click();
      (await server.request(SYNC)).flush(succeeded());
      await server.home([account()]);
      await server.answer('/library/stats', STATS);
      await server.answer(HISTORY, told);
      await server.answer(YEARS, NO_YEARS);
      const asked = await server.request(GAMES);

      // Not `whenStable`: the games are still awaited, on purpose.
      await vi.waitFor(() =>
        expect(all(screen, '[data-day="2026-11-21"] [data-name]')).toEqual(['Jeu sans nom']),
      );

      const now = [...games, bought];
      asked.flush({ total: now.length, offset: 0, limit: 500, games: now });
      await harness.fixture.whenStable();

      expect(all(screen, '[data-day="2026-11-21"] [data-name]')).toEqual(['Outer Wilds']);
    });

    it('says the history in English too', async () => {
      configureApp('en');
      const harness = await RouterTestingHarness.create();
      const server = api();
      const arrived = harness.navigateByUrl('/');
      await server.answer('/me', ACCOUNT);
      await server.home([account()]);
      const games = played();
      await library(server, STATS, games, readings(games), {
        years: [reviewed(2025, 3600), ...synced().years],
      });
      await arrived;
      await harness.fixture.whenStable();
      const screen = harness.fixture.nativeElement as HTMLElement;

      expect(text(screen, '[data-years] h2')).toBe('Playtime per year');
      expect(text(screen, '[data-years-reviewed]')).toBe(
        '2025: from Steam’s Year in Review, month by month.',
      );
      expect(text(screen, '[data-years-since]')).toBe(
        '2026: counted day by day at each sync, from Nov 18, 2026.',
      );
      expect(text(screen, '[data-years-open]')).toBe('A dashed year is not whole.');
      expect(text(screen, '[data-activity] h2')).toBe('Activity');
      expect(all(screen, '[data-activity] h3')[0]).toBe('Saturday, November 21');
      expect(all(screen, '[data-day="2026-11-21"] [data-total]')).toEqual(['142 h in all']);
      expect(text(screen, '[data-activity-note]')).toBe(
        'A day shows what its reading found since the one before.',
      );
    });
  });
});
