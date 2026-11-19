import { DOCUMENT, Service, effect, inject, signal } from '@angular/core';
import { readChoice, writeChoice } from './stored-choice';

/** `system` follows the device's preference and is the default. */
export const THEMES = ['system', 'dark', 'light'] as const;
export type Theme = (typeof THEMES)[number];

/** What labels look like. `pixel` is the default. */
export const CHARACTERS = ['pixel', 'plain'] as const;
export type Character = (typeof CHARACTERS)[number];

const THEME_KEY = 'respawn.theme';
const CHARACTER_KEY = 'respawn.character';

/**
 * The two display settings. Each is one attribute on the root element, which is all the
 * styles read: `data-theme` (absent for `system`, so the style sheet alone follows the
 * device) and `data-character`. Kept in this browser, which shows them from the first
 * frame; the app keeps them on the account too, and takes the account's after a sign-in.
 */
@Service()
export class DisplaySettings {
  private readonly root = inject(DOCUMENT).documentElement;

  readonly theme = signal<Theme>(readChoice(THEME_KEY, THEMES) ?? 'system');
  readonly character = signal<Character>(readChoice(CHARACTER_KEY, CHARACTERS) ?? 'pixel');

  constructor() {
    effect(() => {
      const theme = this.theme();
      if (theme === 'system') {
        this.root.removeAttribute('data-theme');
      } else {
        this.root.setAttribute('data-theme', theme);
      }
    });
    effect(() => this.root.setAttribute('data-character', this.character()));
  }

  setTheme(theme: Theme): void {
    this.theme.set(theme);
    writeChoice(THEME_KEY, theme === 'system' ? null : theme);
  }

  setCharacter(character: Character): void {
    this.character.set(character);
    writeChoice(CHARACTER_KEY, character);
  }
}
