import { describe, expect, it } from 'vitest';
import { Component, provideZonelessChangeDetection, signal } from '@angular/core';
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
  function create(variant: ButtonVariant, name?: string) {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.variant.set(variant);
    fixture.componentInstance.name.set(name);
    return fixture;
  }

  const button = (fixture: ReturnType<typeof create>): HTMLButtonElement =>
    (fixture.nativeElement as HTMLElement).querySelector('button')!;

  it('fills the primary action with the accent', () => {
    const fixture = create('primary');
    fixture.detectChanges();

    expect(button(fixture).className).toContain('bg-accent');
  });

  it('outlines every other action', () => {
    const fixture = create('outlined');
    fixture.detectChanges();

    expect(button(fixture).className).toContain('border-control-line');
    expect(button(fixture).className).not.toContain('bg-accent');
  });

  it('refuses an icon button that has no accessible name', () => {
    const fixture = create('icon');

    expect(() => fixture.detectChanges()).toThrowError(/aria-label/);
  });

  it('accepts an icon button once it is named, and carries the name', () => {
    const fixture = create('icon', 'Synchronise');
    fixture.detectChanges();

    expect(button(fixture).getAttribute('aria-label')).toBe('Synchronise');
    expect(button(fixture).className).toContain('size-touch');
  });
});
