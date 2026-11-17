import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
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
    TestBed.configureTestingModule({ imports: [testTranslations('fr')] });
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

  const detail = (element: HTMLElement) =>
    element.querySelector('[data-account-detail]')?.textContent.trim();

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
    expect(detail(element)).toBe('first@example.org · administrateur');
  });

  it('shows what it knows of an account without an address or without the role', async () => {
    const member = await create({ ...ACCOUNT, is_admin: false });
    expect(detail(member.element)).toBe('first@example.org');

    TestBed.resetTestingModule();
    const fromProvider = await create({ ...ACCOUNT, email: null });
    expect(detail(fromProvider.element)).toBe('administrateur');
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
