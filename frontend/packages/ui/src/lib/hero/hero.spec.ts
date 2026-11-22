import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Hero } from './hero';

@Component({
  imports: [Hero],
  template: `<rs-hero [hue]="42" letter="E"><h2>Elden Ring</h2></rs-hero>`,
})
class Host {}

describe('Hero', () => {
  it('stands its words on a ground of the hue it is given, drawn as in the dark theme', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const hero = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('rs-hero')!;

    expect(hero.style.getPropertyValue('--rs-hero-hue')).toBe('42');
    expect(hero.getAttribute('data-theme')).toBe('dark');
    expect(hero.querySelector('h2')?.textContent).toBe('Elden Ring');
  });

  it('keeps its letter away from screen readers', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const letter = (fixture.nativeElement as HTMLElement).querySelector('.letter')!;

    expect(letter.textContent).toBe('E');
    expect(letter.getAttribute('aria-hidden')).toBe('true');
  });
});
