import { Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { TimeToBeat } from '@respawn/data';
import { type SaidPlaytime, formatCount, saidPlaytime } from '@app/format/format';

/** The three paces players say a game takes, in the order they grow. */
export const PACES = ['hastily', 'normally', 'completely'] as const;
export type Pace = (typeof PACES)[number];

/** Where the owner's time stands against one pace: not compared, short of it, or past it. */
export interface PaceState {
  readonly kind: 'none' | 'left' | 'reached' | 'beyond';
  /** What is left, or beyond: none for the other kinds. */
  readonly time?: SaidPlaytime;
}

export interface PaceRow {
  readonly pace: Pace;
  readonly seconds: number;
  readonly time: SaidPlaytime;
  readonly state: PaceState;
}

/**
 * The paces players gave, each with where the owner's time stands against it. A pace nobody
 * gave is left out. Past the last pace, the time beyond it is said; before it, what is left.
 * A time not known, or none, compares with nothing.
 */
export function paceRows(said: TimeToBeat, played: number | null, language: string): PaceRow[] {
  const seconds: Record<Pace, number | null> = {
    hastily: said.hastily_seconds,
    normally: said.normally_seconds,
    completely: said.completely_seconds,
  };
  const given = PACES.filter((pace) => (seconds[pace] ?? 0) > 0);
  const last = given.at(-1);
  return given.map((pace) => {
    const each = seconds[pace] ?? 0;
    const state = ((): PaceState => {
      if (played === null || played <= 0) return { kind: 'none' };
      if (played < each) return { kind: 'left', time: saidPlaytime(each - played, language) };
      // An hour past the longest pace given is worth saying; a minute past any pace is just reached.
      return pace === last && played - each >= 3600
        ? { kind: 'beyond', time: saidPlaytime(played - each, language) }
        : { kind: 'reached' };
    })();
    return { pace, seconds: each, time: saidPlaytime(each, language), state };
  });
}

/**
 * How long players say a game takes, at three paces, beside the owner's own time: a tile per
 * pace, filled once it is reached, saying what is left or beyond; under them, the owner's time
 * on one scale with a mark where each pace stands. Who said it, and how many, is written last.
 */
@Component({
  selector: 'rs-game-durations',
  imports: [TranslocoDirective],
  templateUrl: './game-durations.html',
})
export class GameDurations {
  readonly timeToBeat = input.required<TimeToBeat>();
  /** The owner's time on the game, in seconds; null when it is not known. */
  readonly played = input.required<number | null>();
  readonly language = input.required<string>();

  protected readonly rows = computed(() =>
    paceRows(this.timeToBeat(), this.played(), this.language()),
  );
  /** The owner's own time, said only when there is some to set against the paces. */
  protected readonly you = computed(() => {
    const played = this.played();
    return played !== null && played > 0 ? saidPlaytime(played, this.language()) : null;
  });
  /**
   * The owner's time and each pace on one scale, in percent of the longer of the two: none
   * while there is no time to set against them.
   */
  protected readonly scale = computed(() => {
    const played = this.played();
    const rows = this.rows();
    if (played === null || played <= 0 || rows.length === 0) return null;
    const end = Math.max(played, ...rows.map((row) => row.seconds));
    return {
      played: (100 * played) / end,
      marks: rows.map((row) => ({ at: (100 * row.seconds) / end, passed: row.seconds <= played })),
    };
  });
  protected readonly players = computed(() => {
    const count = this.timeToBeat().count;
    return count === null || count <= 0
      ? null
      : { one: count === 1, count: formatCount(count, this.language()) };
  });
}
