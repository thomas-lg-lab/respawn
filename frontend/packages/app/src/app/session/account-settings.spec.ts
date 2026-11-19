import { TestBed } from '@angular/core/testing';
import { RouterTestingHarness } from '@angular/router/testing';
import { type Account, type AccountSettings, accountKeys } from '@respawn/data';
import { DisplaySettings } from '@respawn/util';
import { QueryClient } from '@tanstack/angular-query';
import { ACCOUNT, SIGN_IN_OPTIONS, api, configureApp, field, type } from '@app/testing/app-testing';
// Loaded up front, as in the routes' spec: a navigation then finds the screens.
import '@app/layout/shell';
import '@app/pages/home/home';
import '@app/pages/sign-in/sign-in';

type Server = ReturnType<typeof api>;

const root = document.documentElement;

const withSettings = (settings: AccountSettings): Account => ({ ...ACCOUNT, settings });

/** The settings of the interface follow the account: taken at sign-in, kept at each pick. */
describe('the settings on the account', () => {
  async function visit(path: string, exchanges: (server: Server) => Promise<void>) {
    configureApp('fr');
    const harness = await RouterTestingHarness.create();
    const server = api();
    const arrived = harness.navigateByUrl(path);
    await exchanges(server);
    await arrived;
    await harness.fixture.whenStable();
    return { harness, server, screen: harness.fixture.nativeElement as HTMLElement };
  }

  /**
   * Opens the account menu if it is closed, and picks a theme in it. Not waited for: the save
   * it sends is work in progress until the test answers it.
   */
  async function pickTheme(harness: RouterTestingHarness, screen: HTMLElement, theme: string) {
    const trigger = screen.querySelector<HTMLButtonElement>('button[aria-expanded]')!;
    if (trigger.getAttribute('aria-expanded') !== 'true') {
      trigger.click();
      await harness.fixture.whenStable();
    }
    screen.querySelector<HTMLButtonElement>(`[data-theme-choice="${theme}"]`)!.click();
  }

  /** The theme the interface shows. */
  const theme = () => TestBed.inject(DisplaySettings).theme();

  beforeEach(() => {
    localStorage.clear();
    root.removeAttribute('data-theme');
    root.removeAttribute('data-character');
  });
  afterEach(() => api().http.verify());

  it('takes the account’s settings when the app opens with a session, and keeps them here', async () => {
    await visit('/', (server) =>
      server.answer('/me', withSettings({ language: 'en', theme: 'dark', character: 'plain' })),
    );

    await vi.waitFor(() => expect(root.lang).toBe('en'));
    expect(root.getAttribute('data-theme')).toBe('dark');
    expect(root.getAttribute('data-character')).toBe('plain');
    expect(localStorage.getItem('respawn.language')).toBe('en');
    expect(localStorage.getItem('respawn.theme')).toBe('dark');
  });

  it('takes them at a sign-in too', async () => {
    const { harness, server, screen } = await visit('/sign-in', async (server) => {
      await server.refuse('/me', 401, 'not_signed_in');
      await server.answer('/config', SIGN_IN_OPTIONS);
    });

    type(field(screen, 'E-mail'), 'first@example.org');
    type(field(screen, 'Mot de passe'), 'correct horse battery');
    screen.querySelector('form')!.requestSubmit();
    await server.answer(
      '/auth/login',
      withSettings({ language: 'fr', theme: 'light', character: 'plain' }),
    );
    await harness.fixture.whenStable();

    expect(root.getAttribute('data-theme')).toBe('light');
    expect(root.getAttribute('data-character')).toBe('plain');
  });

  it('leaves the browser’s own choices to an account that never chose', async () => {
    localStorage.setItem('respawn.theme', 'light');

    await visit('/', (server) => server.answer('/me', ACCOUNT));

    expect(root.getAttribute('data-theme')).toBe('light');
    expect(root.getAttribute('data-character')).toBe('pixel');
  });

  it('shows a pick at once, and keeps the three on the account', async () => {
    const { harness, server, screen } = await visit('/', (server) => server.answer('/me', ACCOUNT));

    await pickTheme(harness, screen, 'light');

    const sent = await server.request('/me/settings');
    // Shown before the account answers.
    await vi.waitFor(() => expect(root.getAttribute('data-theme')).toBe('light'));
    expect(sent.request.method).toBe('PUT');
    expect(sent.request.body).toEqual({ language: 'fr', theme: 'light', character: 'pixel' });
    sent.flush(withSettings(sent.request.body as AccountSettings));
    await harness.fixture.whenStable();
    expect(screen.querySelector('[role="alert"]')).toBeNull();
  });

  it('keeps the picks one at a time, in order, and is not undone by an earlier one’s answer', async () => {
    const { harness, server, screen } = await visit('/', (server) => server.answer('/me', ACCOUNT));

    await pickTheme(harness, screen, 'dark');
    const first = await server.request('/me/settings');
    await pickTheme(harness, screen, 'light');
    // The second save waits for the first: the account cannot apply them out of order. (Not
    // `whenStable`: a save in flight keeps the app busy until it is answered.)
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(server.http.match((each) => each.url.endsWith('/me/settings'))).toHaveLength(0);

    first.flush(withSettings(first.request.body as AccountSettings));
    const second = await server.request('/me/settings');
    expect(second.request.body).toMatchObject({ theme: 'light' });
    // The first answer is in the server state: the interface did not take it back.
    expect(TestBed.inject(QueryClient).getQueryData(accountKeys.me)).toMatchObject({
      settings: { theme: 'dark' },
    });
    expect(theme()).toBe('light');

    second.flush(withSettings(second.request.body as AccountSettings));
    await harness.fixture.whenStable();
    expect(theme()).toBe('light');
    expect(root.getAttribute('data-theme')).toBe('light');
    expect(TestBed.inject(QueryClient).getQueryData(accountKeys.me)).toMatchObject({
      settings: { theme: 'light' },
    });
  });

  it('says when the account could not keep a pick, which still holds here', async () => {
    const { harness, server, screen } = await visit('/', (server) => server.answer('/me', ACCOUNT));

    await pickTheme(harness, screen, 'dark');
    await server.refuse('/me/settings', 503, 'database_unavailable');
    await harness.fixture.whenStable();

    expect(root.getAttribute('data-theme')).toBe('dark');
    expect(screen.querySelector('[role="alert"]')?.textContent).toContain(
      'Votre compte n’a pas pu garder ce réglage',
    );
  });

  it('leaves the warning behind on the next screen', async () => {
    const { harness, server, screen } = await visit('/', (server) => server.answer('/me', ACCOUNT));
    await pickTheme(harness, screen, 'dark');
    await server.refuse('/me/settings', 503, 'database_unavailable');
    await harness.fixture.whenStable();
    expect(screen.querySelector('[role="alert"]')).not.toBeNull();

    await harness.navigateByUrl('/?again');
    await harness.fixture.whenStable();

    expect(screen.querySelector('[role="alert"]')).toBeNull();
  });
});
