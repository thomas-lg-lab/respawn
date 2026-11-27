import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Disclosure, DisclosurePanel, DisclosureTrigger } from './disclosure';

@Component({
  imports: [Disclosure, DisclosurePanel, DisclosureTrigger],
  template: `
    <div rsDisclosure>
      <button rsDisclosureTrigger>Account</button>
      <section rsDisclosurePanel><button type="button" id="inside">Sign out</button></section>
    </div>
    <button type="button" id="outside">Elsewhere</button>
  `,
})
class Host {}

describe('Disclosure', () => {
  async function create() {
    const fixture = TestBed.createComponent(Host);
    // In the document: the focus only moves between elements that are in it.
    document.body.append(fixture.nativeElement as HTMLElement);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    return {
      fixture,
      trigger: element.querySelector<HTMLButtonElement>('[rsDisclosureTrigger]')!,
      panel: element.querySelector<HTMLElement>('section')!,
      inside: element.querySelector<HTMLButtonElement>('#inside')!,
      outside: element.querySelector<HTMLButtonElement>('#outside')!,
    };
  }

  async function opened() {
    const parts = await create();
    parts.trigger.click();
    await parts.fixture.whenStable();
    return parts;
  }

  afterEach(() => document.body.replaceChildren());

  it('starts closed: the button says so and the panel is hidden', async () => {
    const { trigger, panel } = await create();

    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    expect(trigger.getAttribute('aria-controls')).toBe(panel.id);
    expect(trigger.type).toBe('button');
    expect(panel.hidden).toBe(true);
  });

  it('opens and closes on its button', async () => {
    const { fixture, trigger, panel } = await opened();
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    expect(panel.hidden).toBe(false);

    trigger.click();
    await fixture.whenStable();
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    expect(panel.hidden).toBe(true);
  });

  it('closes on Escape and gives the focus back to its button', async () => {
    const { fixture, trigger, panel, inside } = await opened();
    inside.focus();

    inside.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await fixture.whenStable();

    expect(panel.hidden).toBe(true);
    expect(document.activeElement).toBe(trigger);
  });

  it('opens nothing while its button is marked disabled, and the button keeps the focus', async () => {
    const { fixture, trigger, panel } = await create();
    trigger.setAttribute('aria-disabled', 'true');
    trigger.focus();

    trigger.click();
    await fixture.whenStable();

    expect(panel.hidden).toBe(true);
    expect(document.activeElement).toBe(trigger);
  });

  it('closes on a click outside, not on one inside', async () => {
    const { fixture, panel, inside, outside } = await opened();

    inside.click();
    await fixture.whenStable();
    expect(panel.hidden).toBe(false);

    outside.click();
    await fixture.whenStable();
    expect(panel.hidden).toBe(true);
  });

  it('closes when the focus leaves for another part of the page', async () => {
    const { fixture, panel, inside, outside } = await opened();
    inside.focus();

    outside.focus();
    await fixture.whenStable();

    expect(panel.hidden).toBe(true);
  });
});
