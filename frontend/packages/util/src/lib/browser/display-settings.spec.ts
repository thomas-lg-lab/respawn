import { TestBed } from '@angular/core/testing';
import { DisplaySettings } from './display-settings';

describe('DisplaySettings', () => {
  const root = document.documentElement;

  function create(): DisplaySettings {
    const settings = TestBed.inject(DisplaySettings);
    TestBed.tick();
    return settings;
  }

  beforeEach(() => {
    localStorage.clear();
    root.removeAttribute('data-theme');
    root.removeAttribute('data-character');
  });

  it('follows the system until a theme is chosen: no attribute on the root', () => {
    const settings = create();

    expect(settings.theme()).toBe('system');
    expect(root.hasAttribute('data-theme')).toBe(false);
    expect(root.getAttribute('data-character')).toBe('pixel');
  });

  it('puts a chosen theme on the root and keeps it for the next visit', () => {
    const settings = create();

    settings.setTheme('sombre');
    TestBed.tick();

    expect(root.getAttribute('data-theme')).toBe('sombre');
    expect(localStorage.getItem('respawn.theme')).toBe('sombre');
  });

  it('goes back to the system by removing the attribute and the kept choice', () => {
    const settings = create();
    settings.setTheme('clair');
    TestBed.tick();

    settings.setTheme('system');
    TestBed.tick();

    expect(root.hasAttribute('data-theme')).toBe(false);
    expect(localStorage.getItem('respawn.theme')).toBe(null);
  });

  it('starts from the choices kept in the browser', () => {
    localStorage.setItem('respawn.theme', 'clair');
    localStorage.setItem('respawn.character', 'sobre');

    const settings = create();

    expect(settings.theme()).toBe('clair');
    expect(root.getAttribute('data-theme')).toBe('clair');
    expect(root.getAttribute('data-character')).toBe('sobre');
  });

  it('ignores a kept value that is not a setting', () => {
    localStorage.setItem('respawn.theme', 'neon');
    localStorage.setItem('respawn.character', '');

    const settings = create();

    expect(settings.theme()).toBe('system');
    expect(settings.character()).toBe('pixel');
  });

  it('switches the character on the root', () => {
    const settings = create();

    settings.setCharacter('sobre');
    TestBed.tick();

    expect(root.getAttribute('data-character')).toBe('sobre');
    expect(localStorage.getItem('respawn.character')).toBe('sobre');
  });
});
