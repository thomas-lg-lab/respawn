import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Tab } from './tab';

@Component({
  imports: [Tab],
  template: `<a rsTab href="/library" aria-current="page">Library</a>`,
})
class Host {}

describe('Tab', () => {
  it('stays the link it is put on, with its words', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const tab = (fixture.nativeElement as HTMLElement).querySelector('a')!;

    expect(tab.textContent).toBe('Library');
    expect(tab.getAttribute('href')).toBe('/library');
    expect(tab.getAttribute('aria-current')).toBe('page');
  });
});
