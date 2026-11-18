import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { SIGN_IN_OPTIONS, api, configureApp } from '@app/testing/app-testing';
import { App } from './app';
// The screens are chunks the router fetches when first shown; loaded up front, as in the
// routes' spec, so a navigation finds them.
import './pages/sign-in/sign-in';

describe('App', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => api().http.verify());

  // A guard asks who is signed in before any screen is drawn, and the API can be slow to say.
  it('says that it is on its way until the first screen is there', async () => {
    configureApp('fr');
    const fixture = TestBed.createComponent(App);
    const screen = fixture.nativeElement as HTMLElement;
    const server = api();
    const wait = () => screen.querySelector('p.wait[role="status"]');

    const arrived = TestBed.inject(Router).navigateByUrl('/');
    const asked = await server.request('/me');
    await vi.waitFor(() => expect(wait()?.textContent).toContain('Chargement'));
    expect(screen.querySelector('h1')).toBeNull();

    asked.flush(
      { error: { code: 'not_signed_in', message: 'In English, for the log.' } },
      { status: 401, statusText: '' },
    );
    await server.answer('/config', SIGN_IN_OPTIONS);
    await arrived;
    await fixture.whenStable();

    expect(wait()).toBeNull();
    expect(screen.querySelector('h1')?.textContent.trim()).toBe('Connexion');
  });
});
