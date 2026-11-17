import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import type { SignInOptions } from '@respawn/data';
import type { Language } from '@app/i18n/language';
import {
  ACCOUNT,
  SIGN_IN_OPTIONS,
  api,
  button,
  configureApp,
  field,
  type,
} from '@app/testing/app-testing';
import { SignIn } from './sign-in';

describe('SignIn', () => {
  /** The screen, once the instance said what it offers. */
  async function open(options: Partial<SignInOptions> = {}, language: Language = 'fr') {
    configureApp(language);
    const fixture = TestBed.createComponent(SignIn);
    const server = api();
    await server.answer('/config', { ...SIGN_IN_OPTIONS, ...options });
    await fixture.whenStable();
    return { fixture, server, screen: fixture.nativeElement as HTMLElement };
  }

  const title = (screen: HTMLElement) => screen.querySelector('h1')?.textContent.trim();
  const alert = (screen: HTMLElement) => screen.querySelector('[role="alert"]')?.textContent.trim();
  const submit = (screen: HTMLElement) => screen.querySelector('form')!.requestSubmit();

  beforeEach(() => localStorage.clear());
  afterEach(() => api().http.verify());

  it('offers the local sign-in, and nothing else, on an instance with closed registration', async () => {
    const { screen } = await open();

    expect(title(screen)).toBe('Connexion');
    expect(field(screen, 'E-mail').autocomplete).toBe('username');
    expect(field(screen, 'Mot de passe').autocomplete).toBe('current-password');
    expect(screen.textContent).not.toContain('Créer un compte');
  });

  it('signs in and opens the app', async () => {
    const { fixture, server, screen } = await open();

    type(field(screen, 'E-mail'), 'first@example.org');
    type(field(screen, 'Mot de passe'), 'correct horse battery');
    submit(screen);

    const sent = await server.request('/auth/login');
    expect(sent.request.body).toEqual({
      email: 'first@example.org',
      password: 'correct horse battery',
    });
    sent.flush(ACCOUNT);
    await fixture.whenStable();

    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/'));
  });

  it('says that the credentials were refused, and keeps what was typed', async () => {
    const { fixture, server, screen } = await open();

    type(field(screen, 'E-mail'), 'first@example.org');
    type(field(screen, 'Mot de passe'), 'not the password');
    submit(screen);
    await server.refuse('/auth/login', 401, 'invalid_credentials');
    await fixture.whenStable();

    expect(alert(screen)).toBe(
      'E-mail ou mot de passe incorrect, ou compte verrouillé pour un moment.',
    );
    expect(field(screen, 'E-mail').value).toBe('first@example.org');
    expect(button(screen, 'Se connecter').disabled).toBe(false);
  });

  it('says so after too many attempts', async () => {
    const { fixture, server, screen } = await open();

    type(field(screen, 'E-mail'), 'first@example.org');
    type(field(screen, 'Mot de passe'), 'not the password');
    submit(screen);
    await server.refuse('/auth/login', 429, 'too_many_requests');
    await fixture.whenStable();

    expect(alert(screen)).toBe('Trop de tentatives. Réessayez dans quelques minutes.');
  });

  it('sends nothing while a field is missing, and says which', async () => {
    const { fixture, server, screen } = await open();

    type(field(screen, 'E-mail'), 'not an address');
    submit(screen);
    await fixture.whenStable();

    server.http.expectNone('/api/v1/auth/login');
    const email = field(screen, 'E-mail');
    expect(email.getAttribute('aria-invalid')).toBe('true');
    expect(screen.querySelector(`#${email.getAttribute('aria-describedby')}`)?.textContent).toBe(
      'Cette adresse e-mail n’est pas valide.',
    );
    expect(field(screen, 'Mot de passe').getAttribute('aria-invalid')).toBe('true');
  });

  it('asks for the first account where nobody has one, and says what it will be', async () => {
    const { screen } = await open({ registration_open: true, first_account: true });

    expect(title(screen)).toBe('Créer le premier compte');
    expect(screen.textContent).toContain('Ce compte sera administrateur.');
    expect(field(screen, 'Mot de passe').autocomplete).toBe('new-password');
    // The first account has no sign-in to go back to.
    expect(screen.textContent).not.toContain('Déjà un compte ?');
  });

  it('writes the password rule of the instance under the field, and holds to it', async () => {
    const { fixture, server, screen } = await open({
      registration_open: true,
      first_account: true,
      password_min_length: 16,
    });
    const password = field(screen, 'Mot de passe');
    expect(screen.querySelector(`#${password.getAttribute('aria-describedby')}`)?.textContent).toBe(
      '16 caractères au minimum.',
    );

    type(field(screen, 'Nom affiché'), 'First');
    type(field(screen, 'E-mail'), 'first@example.org');
    type(password, 'fifteen letters');
    submit(screen);
    await fixture.whenStable();

    server.http.expectNone('/api/v1/auth/register');
    expect(screen.textContent).toContain('Il faut 16 caractères au minimum.');
  });

  it('creates the account and opens the app', async () => {
    const { fixture, server, screen } = await open({
      registration_open: true,
      first_account: true,
    });

    type(field(screen, 'Nom affiché'), 'First');
    type(field(screen, 'E-mail'), 'first@example.org');
    type(field(screen, 'Mot de passe'), 'correct horse battery');
    submit(screen);

    const sent = await server.request('/auth/register');
    expect(sent.request.body).toEqual({
      display_name: 'First',
      email: 'first@example.org',
      password: 'correct horse battery',
    });
    sent.flush(ACCOUNT, { status: 201, statusText: '' });
    await fixture.whenStable();

    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/'));
  });

  it('leads to the account form and back where registration is open', async () => {
    const { fixture, screen } = await open({ registration_open: true });
    expect(title(screen)).toBe('Connexion');

    button(screen, 'Créer un compte').click();
    await fixture.whenStable();
    expect(title(screen)).toBe('Créer un compte');
    expect(field(screen, 'Nom affiché')).toBeTruthy();

    // Not the form's own button, which says the same on the sign-in form.
    const back = [...screen.querySelectorAll<HTMLButtonElement>('button[type="button"]')].find(
      (each) => each.textContent.trim() === 'Se connecter',
    )!;
    back.click();
    await fixture.whenStable();
    expect(title(screen)).toBe('Connexion');
  });

  it('forgets the refusal of one form when the other is shown', async () => {
    const { fixture, server, screen } = await open({ registration_open: true });
    type(field(screen, 'E-mail'), 'first@example.org');
    type(field(screen, 'Mot de passe'), 'not the password');
    submit(screen);
    await server.refuse('/auth/login', 401, 'invalid_credentials');
    await fixture.whenStable();
    expect(alert(screen)).toBeTruthy();

    button(screen, 'Créer un compte').click();
    await fixture.whenStable();

    expect(alert(screen)).toBeUndefined();
  });

  it('says that an account already has the address', async () => {
    const { fixture, server, screen } = await open({ registration_open: true });
    button(screen, 'Créer un compte').click();
    await fixture.whenStable();

    type(field(screen, 'Nom affiché'), 'Second');
    type(field(screen, 'E-mail'), 'first@example.org');
    type(field(screen, 'Mot de passe'), 'correct horse battery');
    submit(screen);
    await server.refuse('/auth/register', 409, 'account_exists');
    await fixture.whenStable();

    expect(alert(screen)).toBe('Un compte existe déjà avec cette adresse e-mail.');
  });

  it('says that local sign-in is off, and shows no form', async () => {
    const { screen } = await open({ local_sign_in: false });

    expect(screen.querySelector('form')).toBeNull();
    expect(screen.querySelector('[role="status"]')?.textContent.trim()).toBe(
      'La connexion par compte local est désactivée sur cette instance.',
    );
  });

  it('says that the instance cannot answer, and tries again when asked', async () => {
    configureApp('fr');
    const fixture = TestBed.createComponent(SignIn);
    const screen = fixture.nativeElement as HTMLElement;
    const server = api();
    await server.refuse('/config', 503, 'database_unavailable');
    await fixture.whenStable();

    expect(alert(screen)).toBe('La base de données ne répond pas. Réessayez dans un instant.');
    expect(screen.querySelector('form')).toBeNull();

    button(screen, 'Réessayer').click();
    await server.answer('/config', SIGN_IN_OPTIONS);
    await fixture.whenStable();

    expect(title(screen)).toBe('Connexion');
    expect(screen.querySelector('form')).not.toBeNull();
  });

  it('is written in English too, and changes language on the spot', async () => {
    const { fixture, screen } = await open({ registration_open: true, first_account: true }, 'en');

    expect(title(screen)).toBe('Create the first account');
    expect(screen.textContent).toContain('12 characters at least.');

    screen.querySelector<HTMLButtonElement>('[data-language-choice="fr"]')!.click();
    await vi.waitFor(() => expect(title(screen)).toBe('Créer le premier compte'));
    await fixture.whenStable();
    expect(localStorage.getItem('respawn.language')).toBe('fr');
  });
});
