import { Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Card } from '@respawn/ui';
import { formatCount, formatDate, formatDay, isOne, saidPlaytime } from '@app/format/format';
import type { PlayedDay } from '../history-blocks';

/**
 * The activity: the latest days something was played, each with its games and the time of
 * that day, as the daily readings tell it. Before a second reading there is no day to show:
 * the block says when the tracking began, and that the days will come.
 */
@Component({
  selector: 'rs-activity',
  imports: [TranslocoDirective, Card],
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

  protected readonly shown = computed(() => {
    const language = this.language();
    return this.days().map((day) => ({
      date: day.date,
      name: formatDay(day.date, language),
      games: day.games.map(({ id, game, gone, seconds }) => ({
        id,
        /** No longer a game of the library: there is no name to give it. */
        gone,
        name: game?.name ?? null,
        time: saidPlaytime(seconds, language),
        total:
          game?.playtime_seconds == null ? null : saidPlaytime(game.playtime_seconds, language),
      })),
      others:
        day.others === 0
          ? null
          : {
              one: isOne(day.others, language),
              count: formatCount(day.others, language),
            },
    }));
  });
}
