import { Component, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideLogOut, LucideUserRound } from '@lucide/angular';
import type { Account } from '@respawn/data';
import { Avatar, Button, Disclosure, DisclosurePanel, DisclosureTrigger } from '@respawn/ui';
import type { Character, Theme } from '@respawn/util';
import { SettingsChoices } from '@app/components/settings-choices/settings-choices';
import type { Language } from '@app/i18n/language';

/**
 * The account menu of the header: who is signed in, the settings of the interface, and the
 * way out. A disclosure, not a menu of actions: its button says whether the panel is open,
 * and the panel holds ordinary controls.
 *
 * It only shows what it is given and says what was asked: the shell decides what a pick or
 * a sign-out does.
 */
@Component({
  selector: 'rs-account-menu',
  imports: [
    TranslocoDirective,
    Avatar,
    Button,
    Disclosure,
    DisclosurePanel,
    DisclosureTrigger,
    SettingsChoices,
    LucideLogOut,
    LucideUserRound,
  ],
  templateUrl: './account-menu.html',
})
export class AccountMenu {
  readonly account = input.required<Account>();
  readonly language = input.required<Language>();
  readonly theme = input.required<Theme>();
  readonly character = input.required<Character>();
  /** While the sign-out is on its way: its button waits. */
  readonly signingOut = input(false);

  readonly languagePicked = output<Language>();
  readonly themePicked = output<Theme>();
  readonly characterPicked = output<Character>();
  readonly signOut = output<void>();
}
