import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SearchField } from './search-field';

@Component({
  imports: [SearchField],
  template: `<rs-search-field label="Search a game" placeholder="A title" [(value)]="typed" />`,
})
class Host {
  readonly typed = signal('port');
}

describe('SearchField', () => {
  it('is named for a screen reader, and hints at what to type', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const input = (fixture.nativeElement as HTMLElement).querySelector('input')!;

    expect(input.type).toBe('search');
    expect(input.getAttribute('aria-label')).toBe('Search a game');
    expect(input.placeholder).toBe('A title');
  });

  it('shows the value it is given, and gives back what is typed', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const input = (fixture.nativeElement as HTMLElement).querySelector('input')!;
    expect(input.value).toBe('port');

    input.value = 'portal';
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    expect(fixture.componentInstance.typed()).toBe('portal');
  });
});
