import { Location } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { RouterTestingHarness } from '@angular/router/testing';
import { type LinkedAccount } from '@respawn/data';
import { TEST_LINKED_ACCOUNT, TEST_SYNC_RUN } from '@respawn/data/testing';
import { ACCOUNT, api, configureApp, field, type } from '@app/testing/app-testing';
import '../../layout/shell';
import './account';

const LINKED = `/linked-accounts/${TEST_LINKED_ACCOUNT.id}`;
const KEY = `${LINKED}/key`;
const SYNC = `${LINKED}/sync`;

/** The linked account read a moment ago, so that nothing is awaited. */
function linked(values: Partial<LinkedAccount> = {}): LinkedAccount {
  const at = new Date().toISOString();
  const last = { ...TEST_SYNC_RUN, started_at: at, finished_at: at };
  return {
    ...TEST_LINKED_ACCOUNT,
    sync: { running: false, last, last_succeeded_at: at },
    ...values,
  };
}

describe('Account', () => {
  /** Opens the account screen, signed in, with these accounts linked. */
  async function open(accounts: LinkedAccount[], path = '/account') {
    configureApp('fr');
    const harness = await RouterTestingHarness.create();
    const server = api();
    const arrived = harness.navigateByUrl(path);
    await server.answer('/me', ACCOUNT);
    await server.home(accounts);
    await arrived;
    await harness.fixture.whenStable();
    const screen = harness.fixture.nativeElement as HTMLElement;
    /** The accounts are asked again after something changed them. */
    const told = async (now: LinkedAccount[]) => {
      await server.home(now);
      await harness.fixture.whenStable();
    };
    return { harness, server, screen, told };
  }

  const text = (screen: HTMLElement, selector: string) =>
    screen.querySelector(selector)?.textContent.replace(/\s+/g, ' ').trim();
  const click = (screen: HTMLElement, selector: string) =>
    screen.querySelector<HTMLButtonElement>(selector)!.click();

  beforeEach(() => localStorage.clear());
  afterEach(() => api().http.verify());

  it('says who is signed in and which game accounts are linked', async () => {
    const { screen } = await open([linked()]);

    expect(text(screen, 'h1')).toBe('Mon compte');
    expect(text(screen, '[data-identity] [data-account-name]')).toBe('First');
    expect(text(screen, '[data-identity] [data-account-email]')).toBe('first@example.org');
    // Steam gave no name: the account is told by its id there.
    expect(text(screen, '[data-linked-account] [data-name]')).toBe('76561190000000001');
    expect(text(screen, '[data-key-state]')).toBe('Lu avec la clé de l’instance');
    expect(text(screen, '[data-last-sync]')).toContain('Dernière synchronisation réussie');
    expect(screen.querySelector('[data-key-remove]')).toBeNull();
  });

  it('leads to the Steam link, which comes back here, when none is linked', async () => {
    const { screen } = await open([]);

    expect(text(screen, '[data-none-linked]')).toBe('Aucun compte de jeu n’est encore lié.');
    expect(screen.querySelector('[data-link-steam]')?.getAttribute('href')).toBe(
      '/api/v1/linked-accounts/steam/link?return_to=%2Faccount',
    );
  });

  it('says that an account was linked, once, and reads its library at once', async () => {
    const unread = linked({
      linked_at: new Date().toISOString(),
      sync: { running: false, last: null, last_succeeded_at: null },
    });
    // Not `open`: the screen is not at rest while the reading it asked for is on its way.
    configureApp('fr');
    const harness = await RouterTestingHarness.create();
    const server = api();
    const arrived = harness.navigateByUrl('/account?steam_link=linked');
    await server.answer('/me', ACCOUNT);
    await server.home([unread]);
    await arrived;
    const screen = harness.fixture.nativeElement as HTMLElement;

    const asked = await server.request(SYNC);
    expect(text(screen, '[data-steam-link="linked"]')).toBe('Votre compte Steam est lié.');
    expect(TestBed.inject(Location).path()).toBe('/account');
    expect(text(screen, '[data-sync]')).toContain('Synchronisation en cours…');

    asked.flush(TEST_SYNC_RUN);
    await server.home([linked()]);
    await harness.fixture.whenStable();
    expect(text(screen, '[data-last-sync]')).toContain('Dernière synchronisation réussie');
  });

  it('says why the last reading failed, and whose key Steam refused', async () => {
    const last = { ...TEST_SYNC_RUN, outcome: 'failed', error: 'refused', own_key: true };
    const { screen } = await open([
      linked({ has_own_key: true, sync: { running: false, last, last_succeeded_at: null } }),
    ]);

    expect(text(screen, '[data-sync-reason]')).toBe('Steam a refusé votre clé d’API.');
    expect(text(screen, '[data-last-sync]')).toBe('Aucune synchronisation n’a encore réussi.');
    expect(text(screen, '[data-key-state]')).toBe('Lu avec votre propre clé');
  });

  it('reads an account now, and says when it is too soon', async () => {
    const { screen, server, told } = await open([linked()]);

    click(screen, '[data-sync-now]');
    const asked = await server.request(SYNC);
    expect(asked.request.method).toBe('POST');
    asked.flush(
      { error: { code: 'too_many_requests', message: 'In English, for the log.' } },
      { status: 429, statusText: '' },
    );
    await told([linked()]);

    expect(text(screen, '[data-notice]')).toBe(
      'Une synchronisation vient d’être demandée. Réessayez dans une minute.',
    );
  });

  it('gives a key of the owner’s, and keeps none of it in the page', async () => {
    const { screen, server, harness, told } = await open([linked()]);

    type(field(screen, 'Votre clé d’API Web Steam'), ' 0123456789ABCDEF0123456789ABCDEF ');
    // Drawn before it is sent, as when a person types then submits.
    await harness.fixture.whenStable();
    screen.querySelector<HTMLFormElement>('[data-own-key]')!.requestSubmit();
    const sent = await server.request(KEY);
    expect(sent.request.method).toBe('PUT');
    expect(sent.request.body).toEqual({ key: '0123456789ABCDEF0123456789ABCDEF' });
    sent.flush(null, { status: 204, statusText: '' });
    await told([linked({ has_own_key: true })]);

    expect(text(screen, '[data-notice]')).toBe(
      'Votre clé est gardée. La prochaine synchronisation dira si Steam l’accepte.',
    );
    expect(text(screen, '[data-key-state]')).toBe('Lu avec votre propre clé');
    expect(field(screen, 'Une autre clé d’API Web Steam').value).toBe('');
  });

  it('waits for a key before it sends one, and marks no error on an empty field', async () => {
    const { screen, harness } = await open([linked()]);
    const key = field(screen, 'Votre clé d’API Web Steam');

    expect(screen.querySelector<HTMLButtonElement>('[data-key-give]')!.disabled).toBe(true);
    key.dispatchEvent(new Event('blur'));
    screen.querySelector<HTMLFormElement>('[data-own-key]')!.requestSubmit();
    await harness.fixture.whenStable();

    expect(key.getAttribute('aria-invalid')).toBeNull();
    // `verify` fails on a request nobody answered: none left.

    type(key, 'A1B2');
    await harness.fixture.whenStable();
    expect(screen.querySelector<HTMLButtonElement>('[data-key-give]')!.disabled).toBe(false);
  });

  it('says that a key is not one of the store’s', async () => {
    const { screen, server, told } = await open([linked()]);

    type(field(screen, 'Votre clé d’API Web Steam'), 'not a key');
    screen.querySelector<HTMLFormElement>('[data-own-key]')!.requestSubmit();
    await server.refuse(KEY, 400, 'invalid_key');
    await told([linked()]);

    expect(text(screen, '[data-notice]')).toBe(
      'Ce n’est pas une clé d’API Web Steam : elle n’est faite que de lettres et de chiffres.',
    );
    expect(screen.querySelector('[data-notice]')?.getAttribute('role')).toBe('alert');
    // Still there to correct: only a key that was kept leaves the field.
    expect(field(screen, 'Votre clé d’API Web Steam').value).toBe('not a key');
  });

  it('asks the browser for no saved password in the key’s field', async () => {
    const { screen } = await open([linked()]);

    // `off` is ignored on a password's field: the account's own password would be offered.
    expect(field(screen, 'Votre clé d’API Web Steam').getAttribute('autocomplete')).toBe(
      'new-password',
    );
  });

  it('follows what two accounts wait for at once, and says each outcome on its card', async () => {
    const other = linked({ id: '0198a000-0000-7000-8000-0000000000ff', external_id: '2' });
    const { screen, server, told } = await open([linked(), other]);
    const cards = () => [...screen.querySelectorAll<HTMLElement>('[data-linked-account]')];
    const card = (index: number) => cards()[index]!;
    const syncNow = (card: HTMLElement) =>
      card.querySelector<HTMLButtonElement>('[data-sync-now]')!;

    syncNow(card(0)).click();
    const first = await server.request(SYNC);
    syncNow(card(1)).click();
    const second = await server.request(`/linked-accounts/${other.id}/sync`);

    // The second reading did not take the first one's place. Not `whenStable`: the screen is
    // not at rest while a reading is on its way.
    await vi.waitFor(() =>
      expect(cards().map((one) => syncNow(one).disabled)).toEqual([true, true]),
    );

    first.flush(
      { error: { code: 'too_many_requests', message: 'In English, for the log.' } },
      { status: 429, statusText: '' },
    );
    // Not `told`: the other reading is still on its way.
    await server.home([linked(), other]);

    await vi.waitFor(() =>
      expect(text(card(0), '[data-notice]')).toContain('Réessayez dans une minute'),
    );
    expect(cards().map((one) => syncNow(one).disabled)).toEqual([false, true]);

    second.flush(TEST_SYNC_RUN);
    await told([linked(), other]);
    expect(text(card(0), '[data-notice]')).toContain('Réessayez dans une minute');
    expect(cards().map((one) => syncNow(one).disabled)).toEqual([false, false]);
  });

  it('says on the screen that an account is no longer there, once its card is gone', async () => {
    const { screen, server, told } = await open([linked()]);

    click(screen, '[data-sync-now]');
    await server.refuse(SYNC, 404, 'unknown_linked_account');
    await told([]);

    expect(screen.querySelector('[data-linked-account]')).toBeNull();
    expect(text(screen, '[data-screen-notice]')).toBe(
      'Ce compte lié n’existe plus. Rechargez la page.',
    );
    expect(screen.querySelector('[data-screen-notice]')?.getAttribute('role')).toBe('alert');
  });

  it('asks nothing more of an account while it is unlinked', async () => {
    const { screen, server, harness, told } = await open([linked({ has_own_key: true })]);

    click(screen, '[data-unlink]');
    await harness.fixture.whenStable();
    click(screen, '[data-unlink-confirm]');
    const sent = await server.request(LINKED);

    await vi.waitFor(() =>
      expect(screen.querySelector<HTMLButtonElement>('[data-sync-now]')!.disabled).toBe(true),
    );
    expect(screen.querySelector<HTMLButtonElement>('[data-key-remove]')!.disabled).toBe(true);

    sent.flush(null, { status: 204, statusText: '' });
    await told([]);
  });

  it('takes the owner’s key away', async () => {
    const { screen, server, told } = await open([linked({ has_own_key: true })]);

    click(screen, '[data-key-remove]');
    const sent = await server.request(KEY);
    expect(sent.request.method).toBe('DELETE');
    sent.flush(null, { status: 204, statusText: '' });
    await told([linked()]);

    expect(text(screen, '[data-notice]')).toContain('Votre clé est retirée');
    expect(text(screen, '[data-key-state]')).toBe('Lu avec la clé de l’instance');
  });

  it('unlinks only once asked twice, and says what is forgotten in between', async () => {
    const { screen, server, harness, told } = await open([linked()]);

    click(screen, '[data-unlink]');
    await harness.fixture.whenStable();
    expect(text(screen, '[data-unlink-warning]')).toContain('C’est définitif');

    click(screen, '[data-unlink-confirm]');
    const sent = await server.request(LINKED);
    expect(sent.request.method).toBe('DELETE');
    sent.flush(null, { status: 204, statusText: '' });
    await told([]);

    expect(screen.querySelector('[data-linked-account]')).toBeNull();
    expect(text(screen, '[data-screen-notice]')).toBe(
      'Le compte est délié, et ce qui en avait été lu est oublié.',
    );
    expect(text(screen, '[data-none-linked]')).toBe('Aucun compte de jeu n’est encore lié.');
  });

  it('keeps the account linked when the visitor thinks again', async () => {
    const { screen, harness } = await open([linked()]);

    click(screen, '[data-unlink]');
    await harness.fixture.whenStable();
    click(screen, '[data-unlink-cancel]');
    await harness.fixture.whenStable();

    expect(screen.querySelector('[data-unlink-warning]')).toBeNull();
    expect(screen.querySelector('[data-linked-account]')).not.toBeNull();
  });

  it('says that the accounts could not be told, and asks again on request', async () => {
    configureApp('fr');
    const harness = await RouterTestingHarness.create();
    const server = api();
    const arrived = harness.navigateByUrl('/account');
    await server.answer('/me', ACCOUNT);
    await server.refuse('/linked-accounts', 503, 'database_unavailable');
    await arrived;
    await harness.fixture.whenStable();
    const screen = harness.fixture.nativeElement as HTMLElement;

    expect(text(screen, '[role="alert"]')).toContain('La base de données ne répond pas');

    click(screen, '[data-retry]');
    await server.home([linked()]);
    await harness.fixture.whenStable();
    expect(screen.querySelector('[data-linked-account]')).not.toBeNull();
  });
});
