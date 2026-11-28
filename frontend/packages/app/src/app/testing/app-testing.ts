import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import type { EnvironmentProviders, Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, type Routes, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import type { LibraryGame, LinkedAccount, SignInOptions, StoreTag } from '@respawn/data';
import { TEST_ACCOUNT as ACCOUNT, TEST_LINKED_ACCOUNT } from '@respawn/data/testing';
import { routes } from '@app/app.routes';
import { provideAccountSettings } from '@app/session/account-settings';
import { provideServerState } from '@app/session/session.providers';
import type { Language } from '@app/i18n/language';
import { testTranslations } from './translations';

/** An account as the API describes one. */
export { ACCOUNT };

/** What an instance offers once it has its first account: local sign-in, registration closed. */
export const SIGN_IN_OPTIONS: SignInOptions = {
  local_sign_in: true,
  registration_open: false,
  first_account: false,
  password_min_length: 12,
  oidc_providers: [],
};

/**
 * For the specs of the screens: the real routes, the real translations and the real server
 * state, with the API played by `HttpTestingController`. `screens` stands in for the routes
 * where a spec needs a screen of its own.
 */
export function configureApp(
  language: Language,
  extra: (Provider | EnvironmentProviders)[] = [],
  screens: Routes = routes,
): void {
  localStorage.setItem('respawn.language', language);
  TestBed.configureTestingModule({
    imports: [testTranslations(language)],
    providers: [
      provideRouter(screens),
      provideHttpClient(),
      provideHttpClientTesting(),
      provideServerState(),
      provideAccountSettings(),
      ...extra,
    ],
  });
}

/**
 * The app on its way to an address. The navigation only ends once the API answered what the
 * guards and the screen asked on the way: the spec plays those answers, then awaits `arrived`.
 */
export async function start(path: string, language: Language = 'fr', screens: Routes = routes) {
  configureApp(language, [], screens);
  const harness = await RouterTestingHarness.create();
  const server = api();
  const arrived = harness.navigateByUrl(path);
  return { harness, server, arrived, screen: harness.fixture.nativeElement as HTMLElement };
}

/**
 * Goes to an address, `exchanges` playing what the API answers on the way, and waits for the
 * screen to settle: for a spec that leaves no request unanswered.
 */
export async function visit(
  path: string,
  exchanges: (server: ReturnType<typeof api>) => Promise<void>,
  screens: Routes = routes,
) {
  const { harness, server, arrived, screen } = await start(path, 'fr', screens);
  await exchanges(server);
  await arrived;
  await harness.fixture.whenStable();
  return { harness, server, screen, router: TestBed.inject(Router) };
}

/** The library's games as the front asks for them: its first page, the only one of a small library. */
export const LIBRARY_FIRST_PAGE = '/library?sort=name&offset=0&limit=500';

/**
 * Opens, signed in, a screen that shows the library's games (the library, the time map); the
 * API answers with these games, or leaves them unanswered for `null`.
 */
export async function openWithGames(
  address: string,
  games: LibraryGame[] | null,
  language: Language = 'fr',
  storeTags: StoreTag[] = [],
) {
  const { harness, server, arrived } = await start(address, language);
  await server.answer('/me', ACCOUNT);
  await arrived;
  // The shell asks for the linked accounts on every screen: where the end of a sync is learnt.
  await server.answer('/linked-accounts', { accounts: [TEST_LINKED_ACCOUNT] });
  // Left unanswered, the request keeps the app busy: the spec answers it, then waits.
  if (games) {
    await server.answer(LIBRARY_FIRST_PAGE, { total: games.length, offset: 0, limit: 500, games });
    // The library asks for the names of the stores' tags once a game bears one; no other screen
    // does.
    if (storeTags.length) await server.answer('/store-tags', { tags: storeTags });
    await harness.fixture.whenStable();
  }
  return { harness, server, screen: harness.fixture.nativeElement as HTMLElement };
}

/** The API as the tests play it: wait for a request, then answer or refuse it. */
export function api() {
  const http = TestBed.inject(HttpTestingController);
  /** A request leaves on a later tick than the click that caused it. */
  const request = (path: string) => vi.waitFor(() => http.expectOne(`/api/v1${path}`));
  const answer = async (path: string, body: object | null, status = 200): Promise<void> => {
    (await request(path)).flush(body, { status, statusText: '' });
  };
  return {
    http,
    request,
    answer,
    /** The home opens: it asks which store accounts are linked. None, unless the spec says. */
    home: (accounts: LinkedAccount[] = []) => answer('/linked-accounts', { accounts }),
    async refuse(path: string, status: number, code: string): Promise<void> {
      (await request(path)).flush(
        { error: { code, message: 'In English, for the log.' } },
        { status, statusText: '' },
      );
    },
  };
}

/** Types into a field, the way a person does: the value, then the event. */
export function type(input: HTMLInputElement, value: string): void {
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

/** The field under a label, by the label's words. */
export function field(root: HTMLElement, label: string): HTMLInputElement {
  const found = [...root.querySelectorAll('label')].find((each) => each.textContent === label);
  if (!found) throw new Error(`No field is labelled "${label}".`);
  return root.querySelector<HTMLInputElement>(`#${found.htmlFor}`)!;
}

/** What an element says, its spaces and line breaks as one space each; none when it is not there. */
export function text(within: Element, selector: string): string | undefined {
  return within.querySelector(selector)?.textContent.replace(/\s+/g, ' ').trim();
}

/** The button that bears these words. */
export function button(root: HTMLElement, text: string): HTMLButtonElement {
  const found = [...root.querySelectorAll('button')].find(
    (each) => each.textContent.trim() === text,
  );
  if (!found) throw new Error(`No button says "${text}".`);
  return found;
}
