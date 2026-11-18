import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Api, accountKeys } from '@respawn/data';
import { QueryClient } from '@tanstack/angular-query';
import { ACCOUNT, SIGN_IN_OPTIONS, api, button, configureApp } from '@app/testing/app-testing';
// The screens are chunks the router fetches when first shown. Here the first fetch of one is
// slow enough to outlast a test, so they are loaded up front: a navigation then finds them.
import './layout/shell';
import './pages/foundations/foundations';
import './pages/home/home';
import './pages/not-found/not-found';
import './pages/sign-in/sign-in';

type Server = ReturnType<typeof api>;

/** The screens as an address reaches them: the guards, the frame and the screen together. */
describe('routes', () => {
  /**
   * Goes to an address. The navigation only ends once the API answered what the guards and
   * the screen asked on the way, which `exchanges` plays.
   */
  async function visit(path: string, exchanges: (server: Server) => Promise<void>) {
    configureApp('fr');
    const harness = await RouterTestingHarness.create();
    const server = api();
    const arrived = harness.navigateByUrl(path);
    await exchanges(server);
    await arrived;
    await harness.fixture.whenStable();
    return {
      harness,
      server,
      router: TestBed.inject(Router),
      screen: harness.fixture.nativeElement as HTMLElement,
    };
  }

  const signedIn = (server: Server) => server.answer('/me', ACCOUNT);
  const title = (screen: HTMLElement) => screen.querySelector('h1')?.textContent.trim();

  beforeEach(() => localStorage.clear());
  afterEach(() => api().http.verify());

  it('sends a visitor without a session to the sign-in', async () => {
    const { router, screen } = await visit('/', async (server) => {
      await server.refuse('/me', 401, 'not_signed_in');
      await server.answer('/config', SIGN_IN_OPTIONS);
    });

    expect(router.url).toBe('/sign-in');
    expect(title(screen)).toBe('Connexion');
  });

  it('sends a signed-in visitor away from the sign-in', async () => {
    const { router, screen } = await visit('/sign-in', signedIn);

    expect(router.url).toBe('/');
    expect(title(screen)).toBe('Bonjour, First');
  });

  it('sends to the sign-in a visitor the API cannot identify, where the reason is said', async () => {
    const { router, screen } = await visit('/', async (server) => {
      await server.refuse('/me', 503, 'database_unavailable');
      // A failure is not kept as an answer: the sign-in asks again before it shows.
      await server.refuse('/me', 503, 'database_unavailable');
      await server.refuse('/config', 503, 'database_unavailable');
    });

    expect(router.url).toBe('/sign-in');
    expect(screen.querySelector('[role="alert"]')?.textContent).toContain(
      'La base de données ne répond pas.',
    );
  });

  it('lets back in, on a new try, a visitor whose session the API could not read', async () => {
    const { harness, server, router, screen } = await visit('/', async (server) => {
      await server.refuse('/me', 503, 'database_unavailable');
      await server.refuse('/me', 503, 'database_unavailable');
      // The instance says what it offers, but still not who is there: no form is shown.
      await server.answer('/config', SIGN_IN_OPTIONS);
    });
    expect(screen.querySelector('form')).toBeNull();
    expect(screen.querySelector('[role="alert"]')?.textContent).toContain(
      'La base de données ne répond pas.',
    );

    button(screen, 'Réessayer').click();
    // One try at a time: until the answer, neither the button nor a form, only the wait.
    const asked = await server.request('/me');
    await vi.waitFor(() =>
      expect(screen.querySelector('[role="status"]')?.textContent).toContain('Chargement'),
    );
    expect(() => button(screen, 'Réessayer')).toThrow();
    expect(screen.querySelector('form')).toBeNull();
    asked.flush(ACCOUNT);
    await harness.fixture.whenStable();

    await vi.waitFor(() => expect(router.url).toBe('/'));
    await vi.waitFor(() => expect(title(screen)).toBe('Bonjour, First'));
  });

  it('leads to the sign-in as soon as the API says the session ended', async () => {
    const { harness, server, router, screen } = await visit('/', signedIn);

    // Any request of a screen, refused for want of a session.
    const read = TestBed.inject(QueryClient)
      .fetchQuery({ queryKey: ['library'], queryFn: () => TestBed.inject(Api).get('/library') })
      .catch(() => undefined);
    await server.refuse('/library', 401, 'not_signed_in');
    await read;
    await server.answer('/config', SIGN_IN_OPTIONS);
    await harness.fixture.whenStable();

    await vi.waitFor(() => expect(router.url).toBe('/sign-in'));
    await vi.waitFor(() => expect(title(screen)).toBe('Connexion'));
  });

  // The account is asked again when the window comes back in front, and that can fail for a
  // moment (no network): a failure is not the API saying the session ended.
  it('keeps a signed-in visitor in when asking again who they are fails', async () => {
    const { harness, server, router, screen } = await visit('/', signedIn);

    const asked = TestBed.inject(QueryClient).refetchQueries({ queryKey: accountKeys.me });
    await server.refuse('/me', 503, 'database_unavailable');
    await asked;

    // A guard runs again, and asks the API nothing: the account it knows is still the answer.
    await harness.navigateByUrl('/sign-in');
    expect(router.url).toBe('/');
    expect(title(screen)).toBe('Bonjour, First');
  });

  it('says who is signed in', async () => {
    const { screen } = await visit('/', signedIn);

    expect(title(screen)).toBe('Bonjour, First');
    expect(screen.querySelector('[data-account-email]')?.textContent).toBe('first@example.org');
    expect(screen.querySelector('[data-account-role]')?.textContent).toBe('Administrateur');
  });

  it('signs out from the account menu, back to the sign-in', async () => {
    const { harness, server, router, screen } = await visit('/', signedIn);

    screen.querySelector<HTMLButtonElement>('button[aria-label="Compte et réglages"]')!.click();
    await harness.fixture.whenStable();
    button(screen, 'Se déconnecter').click();
    await server.answer('/auth/logout', null, 204);
    await server.answer('/config', SIGN_IN_OPTIONS);
    await harness.fixture.whenStable();

    expect(router.url).toBe('/sign-in');
    expect(title(screen)).toBe('Connexion');

    // Nobody is signed in any more: going back in asks the API nothing, and is refused.
    await harness.navigateByUrl('/');
    expect(router.url).toBe('/sign-in');
  });

  it('says that the sign-out failed, and stays signed in', async () => {
    const { harness, server, router, screen } = await visit('/', signedIn);

    screen.querySelector<HTMLButtonElement>('button[aria-label="Compte et réglages"]')!.click();
    await harness.fixture.whenStable();
    button(screen, 'Se déconnecter').click();
    await server.refuse('/auth/logout', 503, 'database_unavailable');
    await harness.fixture.whenStable();

    expect(router.url).toBe('/');
    expect(screen.querySelector('[role="alert"]')?.textContent).toContain(
      'La base de données ne répond pas.',
    );
    expect(title(screen)).toBe('Bonjour, First');
  });

  it('keeps the settings of the menu in the browser', async () => {
    const { harness, screen } = await visit('/', signedIn);
    const root = document.documentElement;

    screen.querySelector<HTMLButtonElement>('[data-theme-choice="light"]')!.click();
    screen.querySelector<HTMLButtonElement>('[data-character-choice="plain"]')!.click();
    await harness.fixture.whenStable();

    expect(root.getAttribute('data-theme')).toBe('light');
    expect(root.getAttribute('data-character')).toBe('plain');
    expect(localStorage.getItem('respawn.theme')).toBe('light');
    expect(localStorage.getItem('respawn.character')).toBe('plain');

    screen.querySelector<HTMLButtonElement>('[data-language-choice="en"]')!.click();
    await vi.waitFor(() => expect(title(screen)).toBe('Hello, First'));
    expect(localStorage.getItem('respawn.language')).toBe('en');

    root.removeAttribute('data-theme');
    root.removeAttribute('data-character');
  });

  it('shows the foundations to anyone, without asking who they are', async () => {
    const { screen } = await visit('/foundations', () => Promise.resolve());

    expect(title(screen)).toBe('Les fondations visuelles');
  });

  it('says that an address has no screen', async () => {
    const { screen } = await visit('/nothing/here', () => Promise.resolve());

    expect(title(screen)).toBe('Cette page n’existe pas');
  });
});
