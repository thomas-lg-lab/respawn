import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Button, type ButtonVariant } from './button';

@Component({
  imports: [Button],
  template: `<button rsButton [variant]="variant()" [aria-label]="name()">Go</button>`,
})
class Host {
  readonly variant = signal<ButtonVariant>('outlined');
  readonly name = signal<string | undefined>(undefined);
}

describe('Button', () => {
  afterEach(() => vi.restoreAllMocks());

  function create(variant: ButtonVariant, name?: string) {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.variant.set(variant);
    fixture.componentInstance.name.set(name);
    return fixture;
  }

  const button = (fixture: ReturnType<typeof create>): HTMLButtonElement =>
    (fixture.nativeElement as HTMLElement).querySelector('button')!;

  it('fills the primary action with the accent', async () => {
    const fixture = create('primary');
    await fixture.whenStable();

    expect(button(fixture).className).toContain('bg-accent');
  });

  it('outlines every other action', async () => {
    const fixture = create('outlined');
    await fixture.whenStable();

    expect(button(fixture).className).toContain('border-control-line');
    expect(button(fixture).className).not.toContain('bg-accent');
  });

  it('reports an icon button that has no accessible name, and still draws it', async () => {
    const reported = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const fixture = create('icon');
    await fixture.whenStable();

    expect(reported).toHaveBeenCalledWith(expect.stringMatching(/aria-label/), button(fixture));
    expect(button(fixture).className).toContain('size-touch');
  });

  it('takes a name that arrives after the first render', async () => {
    const reported = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const fixture = create('icon', '');
    await fixture.whenStable();
    expect(reported).toHaveBeenCalledTimes(1);

    fixture.componentInstance.name.set('Synchronise');
    await fixture.whenStable();

    expect(button(fixture).getAttribute('aria-label')).toBe('Synchronise');
    expect(reported).toHaveBeenCalledTimes(1);
  });

  it('says nothing of an icon button that is named', async () => {
    const reported = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const fixture = create('icon', 'Synchronise');
    await fixture.whenStable();

    expect(reported).not.toHaveBeenCalled();
    expect(button(fixture).getAttribute('aria-label')).toBe('Synchronise');
  });
});
