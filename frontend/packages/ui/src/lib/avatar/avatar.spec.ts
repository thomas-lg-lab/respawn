import { TestBed } from '@angular/core/testing';
import { Avatar } from './avatar';

describe('Avatar', () => {
  async function initialOf(name: string) {
    const fixture = TestBed.createComponent(Avatar);
    fixture.componentRef.setInput('name', name);
    await fixture.whenStable();
    return (fixture.nativeElement as HTMLElement).textContent;
  }

  it('shows the first letter of the name, in capitals', async () => {
    expect(await initialOf('first')).toBe('F');
    expect(await initialOf('  élodie')).toBe('É');
  });

  it('never cuts a character in two', async () => {
    expect(await initialOf('🎮 player')).toBe('🎮');
    // A letter and its accent written as two characters, and a flag, which is two as well.
    expect(await initialOf('élodie')).toBe('É');
    expect(await initialOf('🇫🇷 player')).toBe('🇫🇷');
  });

  it('is a decoration: the name is written beside it', async () => {
    const fixture = TestBed.createComponent(Avatar);
    fixture.componentRef.setInput('name', 'First');
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).getAttribute('aria-hidden')).toBe('true');
  });
});
