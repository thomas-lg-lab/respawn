import { Component, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Pill, PillGroup } from '@respawn/ui';
import { CHARACTERS, type Character, THEMES, type Theme } from '@respawn/util';
import { LANGUAGES, type Language } from '@app/i18n/language';

/**
 * The three settings of the interface, each as a group of pills: the language, the theme,
 * the character. It shows the values it is given and says which one was picked; where they
 * are kept (this browser, the account) is not its business.
 */
@Component({
  selector: 'rs-settings-choices',
  imports: [TranslocoDirective, Pill, PillGroup],
  host: { class: 'flex flex-col gap-4' },
  templateUrl: './settings-choices.html',
})
export class SettingsChoices {
  readonly language = input.required<Language>();
  readonly theme = input.required<Theme>();
  readonly character = input.required<Character>();

  readonly languagePicked = output<Language>();
  readonly themePicked = output<Theme>();
  readonly characterPicked = output<Character>();

  protected readonly languages = LANGUAGES;
  protected readonly themes = THEMES;
  protected readonly characters = CHARACTERS;
}
