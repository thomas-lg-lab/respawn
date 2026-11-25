import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Pill } from '../pill/pill';
import { PillGroup } from './pill-group';

@Component({
  imports: [Pill, PillGroup],
  template: `<rs-pill-group label="Theme">
    <button rsPill pressed>Dark</button>
    <button rsPill>Light</button>
  </rs-pill-group>`,
})
class Host {}

@Component({
  imports: [Pill, PillGroup],
  template: `<rs-pill-group segmented label="Theme">
    <button rsPill variant="segment" pressed>Dark</button>
    <button rsPill variant="segment">Light</button>
  </rs-pill-group>`,
})
class SegmentedHost {}

describe('PillGroup', () => {
  it('is a group named by its label, around the pills it is given', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const group = (fixture.nativeElement as HTMLElement).querySelector('rs-pill-group')!;

    expect(group.getAttribute('role')).toBe('group');
    const label = group.querySelector(`#${group.getAttribute('aria-labelledby')}`);
    expect(label?.textContent).toBe('Theme');
    expect(group.querySelectorAll('button[aria-pressed]')).toHaveLength(2);
    expect(group.hasAttribute('data-segmented')).toBe(false);
  });

  it('says when its pills share one track', async () => {
    const fixture = TestBed.createComponent(SegmentedHost);
    await fixture.whenStable();
    const group = (fixture.nativeElement as HTMLElement).querySelector('rs-pill-group')!;

    expect(group.hasAttribute('data-segmented')).toBe(true);
    expect(group.getAttribute('role')).toBe('group');
  });
});
