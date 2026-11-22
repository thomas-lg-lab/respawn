import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FilterTab } from './filter-tab';

@Component({
  imports: [FilterTab],
  template: `<button rsFilterTab [pressed]="pressed()">Launched</button>`,
})
class Host {
  readonly pressed = signal(false);
}

describe('FilterTab', () => {
  it('says whether it is on, and never submits a form', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const tab = (fixture.nativeElement as HTMLElement).querySelector('button')!;

    expect(tab.getAttribute('aria-pressed')).toBe('false');
    expect(tab.getAttribute('type')).toBe('button');

    fixture.componentInstance.pressed.set(true);
    await fixture.whenStable();
    expect(tab.getAttribute('aria-pressed')).toBe('true');
  });
});
