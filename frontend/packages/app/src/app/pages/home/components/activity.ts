import { Component, computed, input, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideChevronDown } from '@lucide/angular';
import { Card } from '@respawn/ui';
import { formatCount, formatDate, formatDay, isOne, saidPlaytime } from '@app/format/format';
import type { PlayedDay } from '../history-blocks';

/**
 * The activity: the latest days something was played, each with its games and the time of
 * that day, as the daily readings tell it. Before a second reading there is no day to show:
 * the block says when the tracking began, and that the days will come. A day names its most
 * played games, and the others on request.
 */
@Component({
  selector: 'rs-activity',
  imports: [TranslocoDirective, LucideChevronDown, Card],
  templateUrl: './activity.html',
})
export class Activity {
  readonly days = input.required<readonly PlayedDay[]>();
  /** When the history starts: the first sync, said in UTC. `null` while no reading listed a game. */
  readonly since = input.required<string | null>();
  readonly language = input.required<string>();

  protected readonly sinceDate = computed(() => {
    const since = this.since();
    return since === null ? null : formatDate(since, this.language());
  });

  /** The days whose every game was asked for: the others name their most played only. */
  private readonly opened = signal<ReadonlySet<string>>(new Set());

  protected readonly shown = computed(() => {
    const language = this.language();
    const opened = this.opened();
    return this.days().map((day) => {
      const whole = opened.has(day.date);
      return {
        date: day.date,
        name: formatDay(day.date, language),
        games: (whole ? [...day.games, ...day.rest] : day.games).map(
          ({ id, game, gone, seconds }) => ({
            id,
            /** No longer a game of the library: there is no name to give it. */
            gone,
            name: game?.name ?? null,
            time: saidPlaytime(seconds, language),
            total:
              game?.playtime_seconds == null ? null : saidPlaytime(game.playtime_seconds, language),
          }),
        ),
        others:
          whole || day.others === 0
            ? null
            : {
                one: isOne(day.others, language),
                count: formatCount(day.others, language),
              },
      };
    });
  });

  /** Names every game of the day, its most played still first. */
  protected open(date: string): void {
    this.opened.update((opened) => new Set([...opened, date]));
  }
}
