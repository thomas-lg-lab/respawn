import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  LucideCheck,
  LucideChevronRight,
  LucideLink,
  LucideLogOut,
  LucideSettings,
} from '@lucide/angular';
import type { Account, Credit } from '@respawn/data';
import { Avatar, Button, Disclosure, DisclosurePanel, DisclosureTrigger } from '@respawn/ui';
import type { Character, Theme } from '@respawn/util';
import { SettingsChoices } from '@app/components/settings-choices/settings-choices';
import type { Language } from '@app/i18n/language';

/**
 * A provider of the instance: its label, whether the account signs in with it already, and
 * the address that links it when it does not.
 */
export interface LinkableProvider {
  readonly name: string;
  readonly label: string;
  readonly linked: boolean;
  readonly address: string;
}

/**
 * The account menu of the header: who is signed in, the settings of the interface, the
 * providers the account may also sign in with, then the way to the account screen and the
 * way out. A disclosure, not a menu of actions: its button says whether the panel is open, and
 * the panel holds ordinary controls.
 *
 * It only shows what it is given and says what was asked: the shell decides what a pick or
 * a sign-out does.
 */
@Component({
  selector: 'rs-account-menu',
  imports: [
    RouterLink,
    TranslocoDirective,
    Avatar,
    Button,
    Disclosure,
    DisclosurePanel,
    DisclosureTrigger,
    SettingsChoices,
    LucideCheck,
    LucideChevronRight,
    LucideLink,
    LucideLogOut,
    LucideSettings,
  ],
  templateUrl: './account-menu.html',
})
export class AccountMenu {
  readonly account = input.required<Account>();
  readonly language = input.required<Language>();
  readonly theme = input.required<Theme>();
  readonly character = input.required<Character>();
  /** The language last picked could not be loaded. */
  readonly languageUnavailable = input(false);
  /**
   * The instance's providers: one linked already is said, following another links it to the
   * account (a navigation, not a call).
   */
  readonly providers = input<readonly LinkableProvider[]>([]);
  /** The sources whose data the instance shows, credited under the settings. */
  readonly credits = input<readonly Credit[]>([]);
  /** While the sign-out is on its way: its button waits. */
  readonly signingOut = input(false);

  readonly languagePicked = output<Language>();
  readonly themePicked = output<Theme>();
  readonly characterPicked = output<Character>();
  readonly signOut = output<void>();
  /** The panel was opened: what it shows that is asked only then can be asked now. */
  readonly opened = output<void>();
}
