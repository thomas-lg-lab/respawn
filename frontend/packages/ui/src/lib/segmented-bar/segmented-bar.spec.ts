import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { SegmentedBar, filledSegments } from './segmented-bar';

@Component({
  imports: [SegmentedBar],
  template: `<rs-segmented-bar [value]="142" [max]="200" [segments]="20">142 h</rs-segmented-bar>`,
})
class Host {}

@Component({
  imports: [SegmentedBar],
  template: `<rs-segmented-bar quiet [value]="1" [max]="2" />`,
})
class QuietHost {}

describe('SegmentedBar', () => {
  it('fills in proportion, never less than one segment for a value above zero', () => {
    expect(filledSegments(142, 200, 20)).toBe(14);
    expect(filledSegments(0, 200, 20)).toBe(0);
    expect(filledSegments(1, 200, 20)).toBe(1);
    expect(filledSegments(200, 200, 20)).toBe(20);
  });

  it('holds a value outside the scale, or a scale that makes no sense', () => {
    expect(filledSegments(500, 200, 20)).toBe(20);
    expect(filledSegments(-3, 200, 20)).toBe(0);
    expect(filledSegments(10, 0, 20)).toBe(0);
    expect(filledSegments(Number.NaN, 200, 20)).toBe(0);
  });

  it('draws the segments for the eye only, beside the value in text', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const drawing = host.querySelector('[aria-hidden="true"]')!;

    expect(drawing.children.length).toBe(20);
    expect(drawing.querySelectorAll('[data-filled]').length).toBe(14);
    expect(host.textContent?.trim()).toBe('142 h');
  });

  it('says when it is quiet, for its colour', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const bar = (fixture.nativeElement as HTMLElement).querySelector('rs-segmented-bar')!;
    expect(bar.hasAttribute('data-quiet')).toBe(false);

    const quiet = TestBed.createComponent(QuietHost);
    await quiet.whenStable();
    expect(
      (quiet.nativeElement as HTMLElement)
        .querySelector('rs-segmented-bar')!
        .hasAttribute('data-quiet'),
    ).toBe(true);
  });
});
