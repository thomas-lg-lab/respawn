import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormField, form, required } from '@angular/forms/signals';
import { TextField } from './text-field';

@Component({
  imports: [FormField, TextField],
  template: `<rs-text-field
    type="email"
    autocomplete="username"
    label="E-mail"
    [formField]="form.email"
    [hint]="hint()"
    [error]="form.email().invalid() ? 'An address is needed.' : null"
  />`,
})
class Host {
  readonly model = signal({ email: '' });
  readonly form = form(this.model, (path) => required(path.email));
  readonly hint = signal<string | null>(null);
}

describe('TextField', () => {
  async function create() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    return { fixture, element, input: element.querySelector('input')! };
  }

  it('ties its label to the input, and tells the browser what the field holds', async () => {
    const { element, input } = await create();

    expect(element.querySelector('label')?.getAttribute('for')).toBe(input.id);
    expect(element.querySelector('label')?.textContent).toBe('E-mail');
    expect(input.type).toBe('email');
    expect(input.getAttribute('autocomplete')).toBe('username');
    expect(input.required).toBe(true);
  });

  it('gives the form what is typed, and shows what the form holds', async () => {
    const { fixture, input } = await create();

    input.value = 'first@example.org';
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(fixture.componentInstance.model().email).toBe('first@example.org');

    fixture.componentInstance.model.set({ email: 'second@example.org' });
    await fixture.whenStable();
    expect(input.value).toBe('second@example.org');
  });

  it('keeps an error to itself until the field was left', async () => {
    const { fixture, element, input } = await create();

    expect(input.hasAttribute('aria-invalid')).toBe(false);
    expect(input.hasAttribute('aria-describedby')).toBe(false);
    expect(element.textContent).not.toContain('An address is needed.');

    input.dispatchEvent(new Event('blur'));
    await fixture.whenStable();

    expect(input.getAttribute('aria-invalid')).toBe('true');
    const error = element.querySelector(`#${input.getAttribute('aria-describedby')}`);
    expect(error?.textContent).toBe('An address is needed.');
  });

  it('withdraws the error once the value is accepted', async () => {
    const { fixture, element, input } = await create();
    input.dispatchEvent(new Event('blur'));
    await fixture.whenStable();

    input.value = 'first@example.org';
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    expect(input.hasAttribute('aria-invalid')).toBe(false);
    expect(element.textContent).not.toContain('An address is needed.');
  });

  it('describes the input by its hint, then by the error that takes its place', async () => {
    const { fixture, element, input } = await create();
    fixture.componentInstance.hint.set('The address you sign in with.');
    await fixture.whenStable();

    const hint = element.querySelector(`#${input.getAttribute('aria-describedby')}`);
    expect(hint?.textContent).toBe('The address you sign in with.');

    input.dispatchEvent(new Event('blur'));
    await fixture.whenStable();

    const error = element.querySelector(`#${input.getAttribute('aria-describedby')}`);
    expect(error?.textContent).toBe('An address is needed.');
    expect(element.textContent).not.toContain('The address you sign in with.');
  });
});
