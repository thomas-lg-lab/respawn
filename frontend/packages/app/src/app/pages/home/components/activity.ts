import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideChevronDown } from '@lucide/angular';
import { Card } from '@respawn/ui';
import { formatCount, formatDate, formatDay, isOne, saidPlaytime } from '@app/format/format';
import { cameFrom, gamePage } from '@app/games/game-page';
import type { PlayedDay } from '../history-blocks';

/**
 * The activity: the latest days something was played, each with its games and the time of
 * that day, as the daily readings tell it. Before a second reading there is no day to show:
 * the block says when the tracking began, and that the days will come. A day names its most
 * played games, and the others on request; a game still in the library leads to its page.
 */
@Component({
  selector: 'rs-activity',
  imports: [RouterLink, TranslocoDirective, LucideChevronDown, Card],
  templateUrl: './activity.html',
})
export class Activity {
  protected readonly gamePage = gamePage;
  protected readonly from = cameFrom('home');

  readonly days = input.required<readonly PlayedDay[]>();
  /** When the history starts: the first sync, said in UTC. `null` while no reading listed a game. */
  readonly since = input.required<string | null>();
  readonly language = input.required<string>();

  protected readonly sinceDate = computed(() => {
    const since = this.since();
    return since === null ? null : formatDate(since, this.language());
  });

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

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
        /** The first game shown on request: where the focus goes once the button is gone. */
        firstOfRest: whole ? (day.rest[0]?.id ?? null) : null,
        others:
          whole || day.rest.length === 0
            ? null
            : {
                one: isOne(day.rest.length, language),
                count: formatCount(day.rest.length, language),
              },
      };
    });
  });

  /**
   * Names every game of the day, its most played still first. The button asking for them goes
   * away, so the focus moves to the first game it brought rather than falling to the page.
   */
  protected open(date: string): void {
    this.opened.update((opened) => new Set([...opened, date]));
    afterNextRender(
      () => {
        this.host.nativeElement
          .querySelector<HTMLElement>(`[data-day="${date}"] [data-first-of-rest]`)
          ?.focus();
      },
      { injector: this.injector },
    );
  }
}
