import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Cover } from './cover';

@Component({
  imports: [Cover],
  template: `<rs-cover [hue]="40" [src]="src()" (failed)="failed = failed + 1" />`,
})
class Host {
  readonly src = signal<string | undefined>(undefined);
  failed = 0;
}

describe('Cover', () => {
  async function create(src?: string) {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.src.set(src);
    await fixture.whenStable();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('is its placeholder alone without an image', async () => {
    const { element } = await create();

    expect(element.querySelector('img')).toBeNull();
  });

  it('shows the image over its placeholder, as a decoration', async () => {
    const { element } = await create('/api/v1/games/1/cover');

    const image = element.querySelector('img')!;
    expect(image.getAttribute('src')).toBe('/api/v1/games/1/cover');
    expect(image.getAttribute('alt')).toBe('');
  });

  it('leaves the placeholder and says so when the image does not load, until another is given', async () => {
    const { fixture, element } = await create('/api/v1/games/1/cover');

    element.querySelector('img')!.dispatchEvent(new Event('error'));
    await fixture.whenStable();
    expect(element.querySelector('img')).toBeNull();
    expect(fixture.componentInstance.failed).toBe(1);

    fixture.componentInstance.src.set('/api/v1/games/2/cover');
    await fixture.whenStable();
    expect(element.querySelector('img')).not.toBeNull();
  });
});
