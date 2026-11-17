import { TestBed } from '@angular/core/testing';
import { DisplaySettings } from '@respawn/util';

// The choices are in the browser before the script runs, as on a reload. `vi.hoisted` runs
// ahead of the imports, and the script acts as soon as it is imported.
vi.hoisted(() => {
  localStorage.setItem('respawn.theme', 'light');
  localStorage.setItem('respawn.character', 'plain');
});
import '../public/theme.js';

/**
 * The script of the document head sets, before the app starts, what `DisplaySettings` sets
 * once it runs. The two are written apart (the script can import nothing), so this holds
 * them to the same keys, the same values and the same attributes.
 */
describe('theme.js', () => {
  const root = document.documentElement;
  const attributes = () => ({
    theme: root.getAttribute('data-theme'),
    character: root.getAttribute('data-character'),
  });

  afterEach(() => {
    localStorage.clear();
    root.removeAttribute('data-theme');
    root.removeAttribute('data-character');
  });

  it('puts the kept theme and character on the root element, as the app does', () => {
    const fromScript = attributes();
    expect(fromScript).toEqual({ theme: 'light', character: 'plain' });

    // The app, from the same storage, on a root element left bare.
    root.removeAttribute('data-theme');
    root.removeAttribute('data-character');
    TestBed.inject(DisplaySettings);
    TestBed.tick();

    expect(attributes()).toEqual(fromScript);
  });
});
