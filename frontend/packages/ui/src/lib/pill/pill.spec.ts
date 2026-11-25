import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Pill, type PillVariant } from './pill';

@Component({
  imports: [Pill],
  template: `<button rsPill [pressed]="pressed()" [variant]="variant()">Recent</button>`,
})
class Host {
  readonly pressed = signal(false);
  readonly variant = signal<PillVariant>('block');
}

describe('Pill', () => {
  it('says whether it is pressed, and never submits a form', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const pill = (fixture.nativeElement as HTMLElement).querySelector('button')!;

    expect(pill.getAttribute('aria-pressed')).toBe('false');
    expect(pill.getAttribute('type')).toBe('button');

    fixture.componentInstance.pressed.set(true);
    await fixture.whenStable();
    expect(pill.getAttribute('aria-pressed')).toBe('true');
  });

  it('is a block unless told to be a line of a row of filters', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const pill = (fixture.nativeElement as HTMLElement).querySelector('button')!;

    expect(pill.dataset['variant']).toBe('block');

    fixture.componentInstance.variant.set('line');
    await fixture.whenStable();
    expect(pill.dataset['variant']).toBe('line');
  });
});
