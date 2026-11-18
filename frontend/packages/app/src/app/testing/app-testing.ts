import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import type { EnvironmentProviders, Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { Account, SignInOptions } from '@respawn/data';
import { routes } from '@app/app.routes';
import { provideServerState } from '@app/session/session.providers';
import type { Language } from '@app/i18n/language';
import { testTranslations } from './translations';

/** An account as the API describes one. */
export const ACCOUNT: Account = {
  id: '0b0f6f3e-6a59-4c8c-9a55-2d7f3f0d6c11',
  email: 'first@example.org',
  display_name: 'First',
  is_admin: true,
};

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
 * state, with the API played by `HttpTestingController`.
 */
export function configureApp(
  language: Language,
  extra: (Provider | EnvironmentProviders)[] = [],
): void {
  localStorage.setItem('respawn.language', language);
  TestBed.configureTestingModule({
    imports: [testTranslations(language)],
    providers: [
      provideRouter(routes),
      provideHttpClient(),
      provideHttpClientTesting(),
      provideServerState(),
      ...extra,
    ],
  });
}

/** The API as the tests play it: wait for a request, then answer or refuse it. */
export function api() {
  const http = TestBed.inject(HttpTestingController);
  /** A request leaves on a later tick than the click that caused it. */
  const request = (path: string) => vi.waitFor(() => http.expectOne(`/api/v1${path}`));
  return {
    http,
    request,
    async answer(path: string, body: object | null, status = 200): Promise<void> {
      (await request(path)).flush(body, { status, statusText: '' });
    },
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

/** The button that bears these words. */
export function button(root: HTMLElement, text: string): HTMLButtonElement {
  const found = [...root.querySelectorAll('button')].find(
    (each) => each.textContent.trim() === text,
  );
  if (!found) throw new Error(`No button says "${text}".`);
  return found;
}
