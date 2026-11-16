import { TestBed } from '@angular/core/testing';
import { Logo } from './logo';

describe('Logo', () => {
  it('writes the name as text and hides the icon from a screen reader', async () => {
    const fixture = TestBed.createComponent(Logo);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.textContent?.trim()).toBe('respawn.');
    expect(host.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
  });
});
