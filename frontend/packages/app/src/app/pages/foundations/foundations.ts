import { Component, computed, inject, signal } from '@angular/core';
import { FormField, FormRoot, email, form, required } from '@angular/forms/signals';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideRefreshCw } from '@lucide/angular';
import {
  Avatar,
  Banner,
  Button,
  Card,
  Label,
  Pill,
  PillGroup,
  SegmentedBar,
  TextField,
} from '@respawn/ui';
import { DisplaySettings } from '@respawn/util';
import { SettingsChoices } from '@app/components/settings-choices/settings-choices';
import { fieldErrorKey } from '@app/forms/field-error';
import { LanguageSetting } from '@app/i18n/language';

const FILTERS = ['all', 'recent', 'never'] as const;
type Filter = (typeof FILTERS)[number];

/**
 * The proof of the foundations: the type styles and the base components, with the three
 * settings that change them. Not a screen of the app: it has its own address, which nothing
 * links to, and needs no session.
 */
@Component({
  selector: 'rs-foundations',
  imports: [
    TranslocoDirective,
    FormField,
    FormRoot,
    Avatar,
    Banner,
    Button,
    Card,
    Label,
    Pill,
    PillGroup,
    SegmentedBar,
    TextField,
    SettingsChoices,
    LucideRefreshCw,
  ],
  host: { class: 'mx-auto block max-w-5xl px-4 py-6 wide:px-10 wide:py-10' },
  templateUrl: './foundations.html',
})
export class Foundations {
  protected readonly display = inject(DisplaySettings);
  protected readonly language = inject(LanguageSetting);

  protected readonly filters = FILTERS;
  protected readonly filter = signal<Filter>('all');
  protected readonly fieldErrorKey = fieldErrorKey;

  // Sample values: the screen has no data of its own.
  protected readonly hours = 142;
  protected readonly hoursScale = 200;
  protected readonly person = 'Élodie';
  private readonly games = 1276;

  /** Grouped as the language groups its digits. */
  protected readonly gamesCount = computed(() =>
    new Intl.NumberFormat(this.language.current()).format(this.games),
  );

  // A form that sends nothing: leave a field empty, or submit, to see the error state.
  protected readonly sample = form(
    signal({ name: '', email: '' }),
    (path) => {
      required(path.name);
      required(path.email);
      email(path.email);
    },
    { submission: { action: () => Promise.resolve() } },
  );
}
