import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Select } from './select';

@Component({
  imports: [Select],
  template: `
    <select rsSelect>
      <option value="name">Title</option>
      <option value="playtime" selected>Playtime</option>
    </select>
  `,
})
class Host {}

describe('Select', () => {
  it('stays the native list, with its options', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const select = (fixture.nativeElement as HTMLElement).querySelector('select')!;

    expect(select.options).toHaveLength(2);
    expect(select.value).toBe('playtime');
  });
});
