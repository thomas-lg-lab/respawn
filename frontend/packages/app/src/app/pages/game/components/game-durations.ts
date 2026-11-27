import { Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideCheck } from '@lucide/angular';
import type { TimeToBeat } from '@respawn/data';
import { SegmentedBar } from '@respawn/ui';
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
  return PACES.flatMap((pace) => {
    const each = seconds[pace];
    if (each === null || !(each > 0)) return [];
    const state = ((): PaceState => {
      if (played === null || played <= 0) return { kind: 'none' };
      if (played < each) return { kind: 'left', time: saidPlaytime(each - played, language) };
      // An hour past the longest pace is worth saying; a minute past any pace is just reached.
      return pace === 'completely' && played - each >= 3600
        ? { kind: 'beyond', time: saidPlaytime(played - each, language) }
        : { kind: 'reached' };
    })();
    return [{ pace, seconds: each, time: saidPlaytime(each, language), state }];
  });
}

/**
 * How long players say a game takes, at three paces, beside the owner's own time: each pace
 * with a bar of the time played against it, a mark once it is reached, and what is left or
 * beyond. Who said it, and how many, is written under them.
 */
@Component({
  selector: 'rs-game-durations',
  imports: [TranslocoDirective, LucideCheck, SegmentedBar],
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
  protected readonly players = computed(() => {
    const count = this.timeToBeat().count;
    return count === null || count <= 0
      ? null
      : { one: count === 1, count: formatCount(count, this.language()) };
  });
}
