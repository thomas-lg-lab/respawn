import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ChartColumn, ColumnChart, barHeight } from './column-chart';

@Component({
  imports: [ColumnChart, ChartColumn],
  template: `
    <rs-column-chart>
      <rs-chart-column label="2025" [value]="888" [max]="888">888 h</rs-chart-column>
      <rs-chart-column label="2026" open openLabel="Not whole" [value]="444" [max]="888"
        >444 h</rs-chart-column
      >
      <rs-chart-column label="2027" open [value]="0" [max]="888">0 min</rs-chart-column>
    </rs-column-chart>
  `,
})
class Host {}

describe('ColumnChart', () => {
  it('draws a bar against the tallest, never nothing for a value above zero', () => {
    expect(barHeight(888, 888)).toBe(100);
    expect(barHeight(444, 888)).toBe(50);
    expect(barHeight(1, 888)).toBe(2);
    expect(barHeight(0, 888)).toBe(0);
  });

  it('holds a value outside the scale, or a scale that makes no sense', () => {
    expect(barHeight(900, 888)).toBe(100);
    expect(barHeight(-3, 888)).toBe(0);
    expect(barHeight(10, 0)).toBe(0);
    expect(barHeight(Number.NaN, 888)).toBe(0);
  });

  it('is a list whose items say their value and their label, the bars for the eye only', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const chart = (fixture.nativeElement as HTMLElement).querySelector('rs-column-chart')!;
    const columns = [...chart.querySelectorAll('rs-chart-column')];

    expect(chart.getAttribute('role')).toBe('list');
    expect(columns.map((each) => each.getAttribute('role'))).toEqual([
      'listitem',
      'listitem',
      'listitem',
    ]);
    expect(columns.map((each) => each.querySelector('.said')?.textContent)).toEqual([
      '888 h',
      '444 h',
      '0 min',
    ]);
    expect(columns.map((each) => each.querySelector('.label')?.textContent)).toEqual([
      '2025',
      '2026',
      '2027',
    ]);
    expect(
      columns.map((each) => each.querySelector<HTMLElement>('[aria-hidden] .bar')?.style.height),
    ).toEqual(['100%', '50%', undefined]);
  });

  it('marks a value that is not whole yet', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const columns = [...(fixture.nativeElement as HTMLElement).querySelectorAll('rs-chart-column')];

    expect(columns.map((each) => each.hasAttribute('data-open'))).toEqual([false, true, true]);
  });

  it('says in words that a column is open, outside the drawing, when given the words', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const columns = [...(fixture.nativeElement as HTMLElement).querySelectorAll('rs-chart-column')];

    expect(columns.map((each) => each.querySelector('.open-word')?.textContent)).toEqual([
      undefined,
      'Not whole',
      undefined,
    ]);
    expect(columns[1]?.querySelector('[aria-hidden] .open-word')).toBeNull();
  });

  it('draws no bar for nothing, not even a hatched one for an open one', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const columns = [...(fixture.nativeElement as HTMLElement).querySelectorAll('rs-chart-column')];

    expect(columns.map((each) => each.querySelector('.bar') !== null)).toEqual([true, true, false]);
  });
});
