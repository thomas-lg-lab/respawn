import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, type Routes } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Api } from '@respawn/data';
import { QueryClient } from '@tanstack/angular-query';
import { SignIn } from '@app/pages/sign-in/sign-in';
import {
  ACCOUNT,
  SIGN_IN_OPTIONS,
  api,
  button,
  configureApp,
  field,
  type,
} from '@app/testing/app-testing';
import { signedInOnly, signedOutOnly } from './session.guards';

/** A screen behind the session that is not the home: the app has none yet. */
@Component({ selector: 'rs-test-screen', template: '' })
class Screen {}

const screens: Routes = [
  { path: 'sign-in', canActivate: [signedOutOnly], component: SignIn },
  { path: 'library', canActivate: [signedInOnly], component: Screen },
  { path: '', pathMatch: 'full', canActivate: [signedInOnly], component: Screen },
];

type Server = ReturnType<typeof api>;

/** Signing in leads back to where the visitor was headed, or was when the session ended. */
describe('the return after a sign-in', () => {
  async function visit(path: string, exchanges: (server: Server) => Promise<void>) {
    configureApp('fr', [], screens);
    const harness = await RouterTestingHarness.create();
    const server = api();
    const arrived = harness.navigateByUrl(path);
    await exchanges(server);
    await arrived;
    await harness.fixture.whenStable();
    const router = TestBed.inject(Router);
    return {
      harness,
      server,
      router,
      screen: harness.fixture.nativeElement as HTMLElement,
      /** Where the sign-in was told to come back to. */
      returnTo: () => router.parseUrl(router.url).queryParams['returnTo'] as string | undefined,
    };
  }

  const nobody = async (server: Server) => {
    await server.refuse('/me', 401, 'not_signed_in');
    await server.answer('/config', SIGN_IN_OPTIONS);
  };

  async function signIn(screen: HTMLElement, server: Server) {
    type(field(screen, 'E-mail'), 'first@example.org');
    type(field(screen, 'Mot de passe'), 'correct horse battery');
    screen.querySelector('form')!.requestSubmit();
    await server.answer('/auth/login', ACCOUNT);
  }

  beforeEach(() => localStorage.clear());
  afterEach(() => api().http.verify());

  it('comes back to the screen a link asked for, with what the address carried', async () => {
    const { router, screen, server, returnTo } = await visit('/library?sort=name', nobody);
    expect(router.url.startsWith('/sign-in?')).toBe(true);
    expect(returnTo()).toBe('/library?sort=name');

    await signIn(screen, server);

    await vi.waitFor(() => expect(router.url).toBe('/library?sort=name'));
  });

  it('comes back to the screen the visitor was on when the session ended', async () => {
    const { harness, router, screen, server, returnTo } = await visit('/library', (server) =>
      server.answer('/me', ACCOUNT),
    );

    const read = TestBed.inject(QueryClient)
      .fetchQuery({ queryKey: ['library'], queryFn: () => TestBed.inject(Api).get('/library') })
      .catch(() => undefined);
    await server.refuse('/library', 401, 'not_signed_in');
    await read;
    await server.answer('/config', SIGN_IN_OPTIONS);
    await harness.fixture.whenStable();
    await vi.waitFor(() => expect(returnTo()).toBe('/library'));

    await signIn(screen, server);

    await vi.waitFor(() => expect(router.url).toBe('/library'));
  });

  it('comes back there after the creation of an account too', async () => {
    const { router, screen, server } = await visit('/library', async (server) => {
      await server.refuse('/me', 401, 'not_signed_in');
      await server.answer('/config', { ...SIGN_IN_OPTIONS, first_account: true });
    });

    type(field(screen, 'Nom affiché'), 'First');
    type(field(screen, 'E-mail'), 'first@example.org');
    type(field(screen, 'Mot de passe'), 'correct horse battery');
    screen.querySelector('form')!.requestSubmit();
    await server.answer('/auth/register', ACCOUNT, 201);

    await vi.waitFor(() => expect(router.url).toBe('/library'));
  });

  it('comes back there when a new try finds the session the API could not read', async () => {
    const { router, screen, server, returnTo } = await visit('/library', async (server) => {
      await server.refuse('/me', 503, 'database_unavailable');
      await server.answer('/config', SIGN_IN_OPTIONS);
    });
    expect(returnTo()).toBe('/library');

    button(screen, 'Réessayer').click();
    await server.answer('/me', ACCOUNT);

    await vi.waitFor(() => expect(router.url).toBe('/library'));
  });

  it('says nothing of the home, which is where a sign-in leads anyway', async () => {
    const { router } = await visit('/', nobody);

    expect(router.url).toBe('/sign-in');
  });

  it('sends on a visitor who already has a session', async () => {
    const { router } = await visit('/sign-in?returnTo=%2Flibrary', (server) =>
      server.answer('/me', ACCOUNT),
    );

    expect(router.url).toBe('/library');
  });

  // The address is in a link anyone can write.
  it('goes to the home when the address to come back to is not one of the app', async () => {
    const { router, screen, server } = await visit(
      '/sign-in?returnTo=%2F%2Felsewhere.example%2Flibrary',
      nobody,
    );

    await signIn(screen, server);

    await vi.waitFor(() => expect(router.url).toBe('/'));
  });
});
