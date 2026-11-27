import { Component, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Banner, Pill, PillGroup } from '@respawn/ui';
import { CHARACTERS, type Character, THEMES, type Theme } from '@respawn/util';
import { LANGUAGES, type Language } from '@app/i18n/language';

/**
 * The three settings of the interface, each as one line, its positions on one track: the
 * language, the theme, the character. It shows the values it is given and says which one was picked; where they
 * are kept (this browser, the account) is not its business.
 */
@Component({
  selector: 'rs-settings-choices',
  imports: [TranslocoDirective, Banner, Pill, PillGroup],
  host: { class: 'flex flex-col gap-2.5' },
  templateUrl: './settings-choices.html',
})
export class SettingsChoices {
  readonly language = input.required<Language>();
  readonly theme = input.required<Theme>();
  readonly character = input.required<Character>();
  /** The language last picked could not be loaded: said under the choice, which did not move. */
  readonly languageUnavailable = input(false);

  readonly languagePicked = output<Language>();
  readonly themePicked = output<Theme>();
  readonly characterPicked = output<Character>();

  protected readonly languages = LANGUAGES;
  protected readonly themes = THEMES;
  protected readonly characters = CHARACTERS;
}
