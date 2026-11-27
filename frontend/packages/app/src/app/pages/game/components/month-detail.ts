import { Component, computed, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Button, Label, SegmentedBar } from '@respawn/ui';
import { formatCount, formatShare, isOne, ordinalForm, saidPlaytime } from '@app/format/format';
import { gamePage } from '@app/games/game-page';
import type { MonthDetail as Detail, MonthKey } from '../game-view';
import { monthName } from './month-calendar';

/** How many segments a game's bar has. */
const SEGMENTS = 16;

/**
 * The month pressed on the calendar: the game's time that month, its place among the games
 * played then, and those games, the most played first. Its words follow the page's game.
 * Each other game leads to its own page; like a game chosen in the search of this page, it
 * carries no screen to lead back to, the browser's way back returning here.
 */
@Component({
  selector: 'rs-month-detail',
  imports: [RouterLink, TranslocoDirective, Button, Label, SegmentedBar],
  templateUrl: './month-detail.html',
})
export class MonthDetail {
  protected readonly gamePage = gamePage;

  readonly month = input.required<MonthKey>();
  /** What the month holds; `null` while it is asked. */
  readonly detail = input.required<Detail | null>();
  /** The month could not be read: the page's own time is still said, from its months. */
  readonly failed = input(false);
  /** The game's time that month, from its own months: said while the month is asked. */
  readonly seconds = input.required<number>();
  /** The page's game, named in its own row. */
  readonly gameName = input.required<string | null>();
  readonly language = input.required<string>();
  readonly retry = output<void>();

  protected readonly segments = SEGMENTS;
  protected readonly name = computed(() => monthName(this.month(), this.language()));
  protected readonly time = computed(() =>
    saidPlaytime(this.detail()?.seconds || this.seconds(), this.language()),
  );

  /** "3rd of the 5 games played that month, 40 % of your 52 h", or "the only game". */
  protected readonly place = computed(() => {
    const detail = this.detail();
    if (!detail || detail.rank === 0) return null;
    const language = this.language();
    return {
      only: detail.count === 1,
      rank: formatCount(detail.rank, language),
      form: ordinalForm(detail.rank, language),
      count: formatCount(detail.count, language),
      share: formatShare(detail.seconds / detail.total, language),
      total: saidPlaytime(detail.total, language),
    };
  });

  protected readonly rows = computed(() =>
    (this.detail()?.rows ?? []).map((row) => ({
      ...row,
      name: row.own ? (this.gameName() ?? row.name) : row.name,
      time: saidPlaytime(row.seconds, this.language()),
    })),
  );

  protected readonly others = computed(() => {
    const others = this.detail()?.others;
    if (!others) return null;
    return {
      one: isOne(others.count, this.language()),
      count: formatCount(others.count, this.language()),
      time: saidPlaytime(others.seconds, this.language()),
    };
  });
}
