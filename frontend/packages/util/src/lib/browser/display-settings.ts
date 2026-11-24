import { DOCUMENT, Service, effect, inject, signal } from '@angular/core';
import { readChoice, writeChoice } from './stored-choice';

/** `system` follows the device's preference and is the default. */
export const THEMES = ['system', 'dark', 'light'] as const;
export type Theme = (typeof THEMES)[number];

/** What labels look like. `pixel` is the default. */
export const CHARACTERS = ['pixel', 'plain'] as const;
export type Character = (typeof CHARACTERS)[number];

/**
 * How the home places its blocks on a wide screen: in labelled groups side by side, or one
 * column. `blocks` is the default; under the width the groups need the home is a column
 * whatever the setting.
 */
export const HOME_LAYOUTS = ['blocks', 'column'] as const;
export type HomeLayout = (typeof HOME_LAYOUTS)[number];

const THEME_KEY = 'respawn.theme';
const CHARACTER_KEY = 'respawn.character';
const HOME_KEY = 'respawn.home';

/**
 * The display settings. The theme and the character are each one attribute on the root
 * element, which is all the styles read: `data-theme` (absent for `system`, so the style
 * sheet alone follows the device) and `data-character`; the home's layout is read by the
 * home alone. Kept in this browser, which shows them from the first frame; the app keeps
 * them on the account too, and takes the account's after a sign-in.
 */
@Service()
export class DisplaySettings {
  private readonly root = inject(DOCUMENT).documentElement;

  readonly theme = signal<Theme>(readChoice(THEME_KEY, THEMES) ?? 'system');
  readonly character = signal<Character>(readChoice(CHARACTER_KEY, CHARACTERS) ?? 'pixel');
  readonly home = signal<HomeLayout>(readChoice(HOME_KEY, HOME_LAYOUTS) ?? 'blocks');

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

  setHome(home: HomeLayout): void {
    this.home.set(home);
    writeChoice(HOME_KEY, home);
  }
}
