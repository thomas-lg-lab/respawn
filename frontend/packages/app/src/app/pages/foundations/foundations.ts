import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideRefreshCw } from '@lucide/angular';
import { Button, Card, Label, Pill, SegmentedBar } from '@respawn/ui';
import { CHARACTERS, DisplaySettings, THEMES } from '@respawn/util';
import { LANGUAGES, LanguageSetting } from '@app/i18n/language';

const FILTERS = ['all', 'recent', 'never'] as const;
type Filter = (typeof FILTERS)[number];

/**
 * The proof of the foundations: the type styles and the base components, with the three
 * switches that change them. It stands in for the first real screen and goes when that
 * screen arrives.
 */
@Component({
  selector: 'rs-foundations',
  imports: [TranslocoDirective, Button, Card, Label, Pill, SegmentedBar, LucideRefreshCw],
  templateUrl: './foundations.html',
})
export class Foundations {
  protected readonly display = inject(DisplaySettings);
  protected readonly language = inject(LanguageSetting);

  protected readonly themes = THEMES;
  protected readonly characters = CHARACTERS;
  protected readonly languages = LANGUAGES;
  protected readonly filters = FILTERS;

  protected readonly filter = signal<Filter>('all');

  // Sample values: the screen has no data of its own.
  protected readonly hours = 142;
  protected readonly hoursScale = 200;
  private readonly games = 1276;

  /** Grouped as the language groups its digits. */
  protected readonly gamesCount = computed(() =>
    new Intl.NumberFormat(this.language.current()).format(this.games),
  );
}
