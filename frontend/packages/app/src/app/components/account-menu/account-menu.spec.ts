import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { Account } from '@respawn/data';
import { ACCOUNT } from '@app/testing/app-testing';
import { testTranslations } from '@app/testing/translations';
import { AccountMenu } from './account-menu';

@Component({
  imports: [AccountMenu],
  template: `<rs-account-menu
    language="fr"
    theme="system"
    character="pixel"
    [account]="account()"
    (themePicked)="picked.push($event)"
    (signOut)="picked.push('sign-out')"
  />`,
})
class Host {
  readonly account = signal<Account>(ACCOUNT);
  readonly picked: string[] = [];
}

describe('AccountMenu', () => {
  async function create(account: Account = ACCOUNT) {
    TestBed.configureTestingModule({
      imports: [testTranslations('fr')],
      // Any address leads somewhere: the menu's link is followed, to no screen of this spec.
      providers: [provideRouter([{ path: '**', children: [] }])],
    });
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.account.set(account);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    return {
      fixture,
      element,
      trigger: element.querySelector<HTMLButtonElement>('button[aria-expanded]')!,
      panel: element.querySelector<HTMLElement>('section')!,
    };
  }

  const email = (element: HTMLElement) =>
    element.querySelector('[data-account-email]')?.textContent.trim();
  const role = (element: HTMLElement) =>
    element.querySelector('[data-account-role]')?.textContent.trim();

  it('is a named button that says whether its panel is open', async () => {
    const { fixture, trigger, panel } = await create();
    expect(trigger.getAttribute('aria-label')).toBe('Compte et réglages');
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    expect(panel.hidden).toBe(true);

    trigger.click();
    await fixture.whenStable();

    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    expect(panel.hidden).toBe(false);
    expect(panel.getAttribute('aria-label')).toBe('Compte et réglages');
  });

  it('says who is signed in, and that they are the administrator', async () => {
    const { element } = await create();

    expect(element.querySelector('[data-account-name]')?.textContent).toBe('First');
    // The role stands beside the name: a long name or address is cut, never the role.
    expect(email(element)).toBe('first@example.org');
    expect(element.querySelector('[data-account-email]')?.getAttribute('title')).toBe(
      'first@example.org',
    );
    expect(role(element)).toBe('Administrateur');
  });

  it('shows what it knows of an account without an address or without the role', async () => {
    const member = await create({ ...ACCOUNT, is_admin: false });
    expect(email(member.element)).toBe('first@example.org');
    expect(role(member.element)).toBeUndefined();

    TestBed.resetTestingModule();
    const fromProvider = await create({ ...ACCOUNT, email: null });
    expect(email(fromProvider.element)).toBeUndefined();
    expect(role(fromProvider.element)).toBe('Administrateur');
  });

  it('leads to the account screen, and closes behind the visitor', async () => {
    const { fixture, element, trigger, panel } = await create();
    trigger.click();
    await fixture.whenStable();

    const link = element.querySelector<HTMLAnchorElement>('[data-account-link]')!;
    expect(link.getAttribute('href')).toBe('/account');
    expect(link.textContent.trim()).toBe('Mon compte');

    link.click();
    await fixture.whenStable();
    expect(panel.hidden).toBe(true);
    // Not left on a link that is now hidden.
    expect(document.activeElement).toBe(trigger);
  });

  it('says which setting was picked and that the way out was asked, and does neither itself', async () => {
    const { fixture, element } = await create();

    element.querySelector<HTMLButtonElement>('[data-theme-choice="dark"]')!.click();
    element.querySelector<HTMLButtonElement>('[data-sign-out]')!.click();
    await fixture.whenStable();

    expect(fixture.componentInstance.picked).toEqual(['dark', 'sign-out']);
    // The pressed pill is the value it was given, not the one clicked.
    expect(
      element.querySelector('[data-theme-choice="system"]')?.getAttribute('aria-pressed'),
    ).toBe('true');
  });
});
