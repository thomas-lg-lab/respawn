import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Pill } from './pill';

@Component({
  imports: [Pill],
  template: `<button rsPill [pressed]="pressed()">Recent</button>`,
})
class Host {
  readonly pressed = signal(false);
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
});
