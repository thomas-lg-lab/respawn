import { RouterTestingHarness } from '@angular/router/testing';
import { type LibraryGame } from '@respawn/data';
import { TEST_LINKED_ACCOUNT } from '@respawn/data/testing';
import { ACCOUNT, api, configureApp } from '@app/testing/app-testing';
import type { Language } from '@app/i18n/language';
import '../../layout/shell';
import './time';

const FIRST_PAGE = '/library?sort=name&offset=0&limit=500';
const HOUR = 3600;

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

/** Three launched games, 152 hours in all, and one never launched. */
function library(): LibraryGame[] {
  return [
    game('Celeste'),
    game('Elden Ring', { playtime_seconds: 50 * HOUR, last_played_at: ago(3) }),
    game('Hades', { playtime_seconds: 100 * HOUR, last_played_at: ago(400) }),
    game('Portal 2', { playtime_seconds: 2 * HOUR, last_played_at: ago(1) }),
  ];
}

describe('Time', () => {
  /** Opens the Time tab, signed in; the API answers with these games unless told not to. */
  async function open(games: LibraryGame[] | null = library(), language: Language = 'fr') {
    configureApp(language);
    const harness = await RouterTestingHarness.create();
    const server = api();
    const arrived = harness.navigateByUrl('/time');
    await server.answer('/me', ACCOUNT);
    await arrived;
    // The shell asks for the linked accounts on every screen: where the end of a sync is learnt.
    await server.answer('/linked-accounts', { accounts: [TEST_LINKED_ACCOUNT] });
    if (games) {
      await server.answer(FIRST_PAGE, { total: games.length, offset: 0, limit: 500, games });
      await harness.fixture.whenStable();
    }
    return { harness, server, screen: harness.fixture.nativeElement as HTMLElement };
  }

  const text = (within: Element, selector: string) =>
    within.querySelector(selector)?.textContent.replace(/\s+/g, ' ').trim();
  const tile = (screen: HTMLElement, name: string) =>
    screen.querySelector<HTMLButtonElement>(`[data-tile][aria-label^="${name},"]`)!;

  beforeEach(() => localStorage.clear());
  afterEach(() => api().http.verify());

  it('draws a tile per launched game, coloured by its last session, and says the whole', async () => {
    const { screen } = await open();

    expect(text(screen, '[data-heading]')).toBe('152 h de jeu sur 3 jeux. Voici où il est passé.');
    const tiles = [...screen.querySelectorAll('[data-tile]')];
    expect(tiles.map((each) => each.getAttribute('aria-label'))).toEqual([
      'Hades, 100 h',
      'Elden Ring, 50 h',
      'Portal 2, 2 h',
    ]);
    expect(tiles.map((each) => each.getAttribute('data-recency'))).toEqual(['4', '1', '1']);
    expect(
      [...screen.querySelectorAll('[data-legend] li')].map((each) => each.textContent.trim()),
    ).toEqual([
      'Ces deux dernières semaines',
      'Ces trois derniers mois',
      'Cette dernière année',
      'Plus ancien',
    ]);
  });

  it('labels the tile under the pointer, and on a touch screen the one tapped', async () => {
    const { harness, screen } = await open();
    const label = () =>
      [...screen.querySelectorAll('[data-tile-label] span')].map((each) => each.textContent.trim());
    const point = async (name: string, type: string, pointerType: string) => {
      tile(screen, name).dispatchEvent(new PointerEvent(type, { pointerType }));
      await harness.fixture.whenStable();
    };

    expect(screen.querySelector('[data-tile-label]')).toBeNull();
    await point('Portal 2', 'pointerenter', 'mouse');
    expect(label()).toEqual(['Portal 2', '2 h']);
    await point('Portal 2', 'pointerleave', 'mouse');
    expect(screen.querySelector('[data-tile-label]')).toBeNull();

    // A finger has no hover: its tap leaves the label until something else is picked.
    const tap = async (name: string) => {
      await point(name, 'pointerenter', 'touch');
      await point(name, 'pointerdown', 'touch');
      await point(name, 'pointerleave', 'touch');
      tile(screen, name).click();
      await harness.fixture.whenStable();
    };
    await tap('Elden Ring');
    expect(label()).toEqual(['Elden Ring', '50 h']);
    await tap('Portal 2');
    expect(label()).toEqual(['Portal 2', '2 h']);
    // A mouse that comes and goes does not bring the tapped one back.
    await point('Elden Ring', 'pointerenter', 'mouse');
    await point('Elden Ring', 'pointerleave', 'mouse');
    expect(screen.querySelector('[data-tile-label]')).toBeNull();
  });

  it('drops the label of the tile tapped once the band is picked', async () => {
    const games = Array.from({ length: 32 }, (_, rank) =>
      game(`Game ${rank + 1}`, { playtime_seconds: (100 - rank) * HOUR }),
    );
    const { harness, screen } = await open(games);

    tile(screen, 'Game 1').dispatchEvent(new PointerEvent('pointerdown', { pointerType: 'touch' }));
    tile(screen, 'Game 1').click();
    await harness.fixture.whenStable();
    expect(screen.querySelector('[data-tile-label]')).not.toBeNull();

    tile(screen, '2 autres jeux').click();
    await harness.fixture.whenStable();
    expect(screen.querySelector('[data-tile-label]')).toBeNull();
  });

  it('writes the label of a tile as tall as the map inside it, where the map does not cut it', async () => {
    const { harness, screen } = await open();

    // Hades holds two thirds of the time: its tile takes the whole height on the left.
    tile(screen, 'Hades').dispatchEvent(new PointerEvent('pointerenter', { pointerType: 'mouse' }));
    await harness.fixture.whenStable();
    const label = screen.querySelector<HTMLElement>('[data-tile-label]')!;
    expect(label.style.top).toBe('0%');
    expect(label.style.bottom).toBe('');
    // Picked, it grows away from the side it touches, and from its middle along the height it
    // fills: either way would be cut.
    expect(tile(screen, 'Hades').style.transformOrigin).toBe('left center');
    // A name keeps to two lines, so the label is never taller than the room it was given.
    expect(label.querySelector('[data-label-name]')?.classList).toContain('line-clamp-2');
  });

  it('says more of the most played game, then of the tile pressed', async () => {
    const { harness, screen } = await open();

    expect(tile(screen, 'Hades').getAttribute('aria-pressed')).toBe('true');
    expect(text(screen, '[data-detail] h2')).toBe('Hades');
    expect(text(screen, '[data-share]')).toBe(
      '100 h, soit 66 % de votre temps de jeu. Dernière session il y a 1 an.',
    );

    tile(screen, 'Portal 2').click();
    await harness.fixture.whenStable();

    expect(tile(screen, 'Hades').getAttribute('aria-pressed')).toBe('false');
    expect(tile(screen, 'Portal 2').getAttribute('aria-pressed')).toBe('true');
    expect(text(screen, '[data-detail] h2')).toBe('Portal 2');
    expect(text(screen, '[data-share]')).toBe(
      '2 h, soit 1 % de votre temps de jeu. Dernière session hier.',
    );
  });

  it('puts the launched games past the thirtieth under the map, which leads to the library', async () => {
    const games = Array.from({ length: 32 }, (_, rank) =>
      game(`Game ${rank + 1}`, { playtime_seconds: (100 - rank) * HOUR }),
    );
    const { harness, screen } = await open(games);

    const tiles = screen.querySelectorAll('[data-tile]');
    expect(tiles).toHaveLength(31);
    expect(tiles[30]?.getAttribute('data-tile')).toBe('others');
    expect(tiles[30]?.hasAttribute('data-recency')).toBe(false);
    expect(text(screen, '[data-tile="others"] [data-tile-name]')).toBe('+ 2 autres jeux');
    expect(screen.textContent).toContain('Les autres jeux sont réunis sous la carte');
    tile(screen, '2 autres jeux').click();
    await harness.fixture.whenStable();

    expect(text(screen, '[data-detail] h2')).toBe('2 autres jeux');
    expect(screen.querySelector('[data-detail] a')?.getAttribute('href')).toBe('/library');
  });

  it('draws nothing under two launched games, and leads to the home', async () => {
    const { screen } = await open([game('Hades', { playtime_seconds: HOUR }), game('Celeste')]);

    expect(screen.querySelector('[data-time-map]')).toBeNull();
    expect(text(screen, '[data-empty]')).toBe(
      'La carte a besoin d’au moins deux jeux lancés. L’accueil dit où en est la bibliothèque.',
    );
    expect(screen.querySelector('[data-home]')?.getAttribute('href')).toBe('/');
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

    expect(screen.querySelectorAll('[data-tile]')).toHaveLength(3);
  });

  it('speaks English too', async () => {
    const { screen } = await open(library(), 'en');

    expect(text(screen, '[data-heading]')).toBe(
      '152 h played over 3 games. Here is where it went.',
    );
    expect(text(screen, '[data-share]')).toBe(
      '100 h, 66% of your playtime. Last session 1 year ago.',
    );
  });
});
