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

    settings.setTheme('dark');
    TestBed.tick();

    expect(root.getAttribute('data-theme')).toBe('dark');
    expect(localStorage.getItem('respawn.theme')).toBe('dark');
  });

  it('goes back to the system by removing the attribute and the kept choice', () => {
    const settings = create();
    settings.setTheme('light');
    TestBed.tick();

    settings.setTheme('system');
    TestBed.tick();

    expect(root.hasAttribute('data-theme')).toBe(false);
    expect(localStorage.getItem('respawn.theme')).toBe(null);
  });

  it('starts from the choices kept in the browser', () => {
    localStorage.setItem('respawn.theme', 'light');
    localStorage.setItem('respawn.character', 'plain');

    const settings = create();

    expect(settings.theme()).toBe('light');
    expect(root.getAttribute('data-theme')).toBe('light');
    expect(root.getAttribute('data-character')).toBe('plain');
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

    settings.setCharacter('plain');
    TestBed.tick();

    expect(root.getAttribute('data-character')).toBe('plain');
    expect(localStorage.getItem('respawn.character')).toBe('plain');
  });

  it('places the home in blocks until another layout is chosen, and keeps the choice', () => {
    const settings = create();
    expect(settings.home()).toBe('blocks');

    settings.setHome('column');

    expect(settings.home()).toBe('column');
    expect(localStorage.getItem('respawn.home')).toBe('column');
  });

  it('starts from the home layout kept in the browser, unless it is not one', () => {
    localStorage.setItem('respawn.home', 'column');
    expect(create().home()).toBe('column');

    TestBed.resetTestingModule();
    localStorage.setItem('respawn.home', 'grid');
    expect(create().home()).toBe('blocks');
  });
});
