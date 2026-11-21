import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { StatTile } from './stat-tile';

@Component({
  imports: [StatTile],
  template: `<rs-stat-tile caption="Never launched" figure="444" [note]="note()" />`,
})
class Host {
  readonly note = signal<string | undefined>('out of 1,305 games');
}

describe('StatTile', () => {
  it('shows what it counts, the figure, and what the figure covers', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const tile = (fixture.nativeElement as HTMLElement).querySelector('rs-stat-tile')!;

    expect(tile.querySelector('.caption')?.textContent).toBe('Never launched');
    expect(tile.querySelector('.figure')?.textContent).toBe('444');
    expect(tile.querySelector('.note')?.textContent).toBe('out of 1,305 games');
  });

  it('draws no line under the figure when there is nothing to say', async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.note.set(undefined);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('.note')).toBeNull();
  });
});
