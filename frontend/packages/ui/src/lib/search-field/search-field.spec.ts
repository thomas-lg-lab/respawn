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

@Component({
  imports: [SearchField],
  template: `<rs-search-field
    label="Search a game"
    popup="suggestions"
    [expanded]="shown()"
    [active]="on()"
    ><kbd>Ctrl K</kbd></rs-search-field
  >`,
})
class WithSuggestions {
  readonly shown = signal(false);
  readonly on = signal<string | undefined>(undefined);
}

describe('SearchField', () => {
  it('takes a press on its box, beside what is typed, for a press on the field', async () => {
    const fixture = TestBed.createComponent(WithSuggestions);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const input = element.querySelector('input')!;

    for (const beside of [
      element.querySelector('rs-search-field')!,
      element.querySelector('kbd')!,
    ]) {
      input.blur();
      const press = new MouseEvent('mousedown', { bubbles: true, cancelable: true });
      beside.dispatchEvent(press);

      // The focus is not taken away, and comes to the field.
      expect(press.defaultPrevented).toBe(true);
      expect(document.activeElement).toBe(input);
    }

    const typed = new MouseEvent('mousedown', { bubbles: true, cancelable: true });
    input.dispatchEvent(typed);
    expect(typed.defaultPrevented).toBe(false);
  });

  it('is a plain search field while it has no list of suggestions', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const input = (fixture.nativeElement as HTMLElement).querySelector('input')!;

    expect(input.hasAttribute('role')).toBe(false);
    expect(input.hasAttribute('aria-expanded')).toBe(false);
    expect(input.hasAttribute('aria-controls')).toBe(false);
  });

  it('says of its list whether it is shown and which suggestion the keys are on', async () => {
    const fixture = TestBed.createComponent(WithSuggestions);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const input = element.querySelector('input')!;

    expect(input.getAttribute('role')).toBe('combobox');
    expect(input.getAttribute('aria-controls')).toBe('suggestions');
    expect(input.getAttribute('aria-expanded')).toBe('false');
    expect(input.hasAttribute('aria-activedescendant')).toBe(false);
    // What the screen put inside stands after the field.
    expect(element.querySelector('rs-search-field > kbd')?.textContent).toBe('Ctrl K');

    fixture.componentInstance.shown.set(true);
    fixture.componentInstance.on.set('suggestions-1');
    await fixture.whenStable();

    expect(input.getAttribute('aria-expanded')).toBe('true');
    expect(input.getAttribute('aria-activedescendant')).toBe('suggestions-1');
  });

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
