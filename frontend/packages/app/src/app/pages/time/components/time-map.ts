import {
  Component,
  DestroyRef,
  type ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { formatCount, saidPlaytime } from '@app/format/format';
import { treemap } from '@app/games/time-map';
import type { GameTile, OthersTile } from '../time-tiles';

/** The drawing's size until it is measured: about what a wide screen gives it. */
const ASSUMED = { width: 1100, height: 560 };

/** Under these, in pixels, a tile has no room for its game's name, or for its time. */
const NAMED = { width: 88, height: 44 };
const TIMED = { width: 56, height: 26 };
/** What a second line of the name needs: without it, the name ends in an ellipsis. */
const SECOND_LINE = 24;
/** A tile whose shorter side reaches this names its game in large, on two lines if need be. */
const LARGE = 150;

/**
 * The time map, full size: a tile per named game, as large as its playtime and coloured by
 * its last session, and the other launched games in a band under the tiles, not drawn to
 * scale (the long tail would otherwise take most of the drawing). A tile, and the band, is a
 * button: pressing it tells the screen, which says more of it. A tile names its game and its
 * time when it has room.
 */
@Component({
  selector: 'rs-time-map',
  imports: [TranslocoDirective],
  templateUrl: './time-map.html',
})
export class TimeMap {
  readonly tiles = input.required<readonly GameTile[]>();
  readonly others = input.required<OthersTile | null>();
  readonly selected = input.required<string>();
  readonly language = input.required<string>();
  readonly picked = output<string>();

  private readonly drawing = viewChild<ElementRef<HTMLElement>>('drawing');
  /** To the pixel: a fraction more or less does not cut the map again. */
  private readonly size = signal(ASSUMED, {
    equal: (a, b) => a.width === b.width && a.height === b.height,
  });

  protected readonly placed = computed(() => {
    const { width, height } = this.size();
    // The tiles come heaviest first, as the cut needs them.
    const cut = treemap(this.tiles(), (tile) => tile.seconds, width / height);
    return cut.map((tile) => {
      const across = (tile.width / 100) * width;
      const down = (tile.height / 100) * height;
      const named = across >= NAMED.width && down >= NAMED.height;
      const timed =
        across >= TIMED.width && down >= (named ? NAMED.height + TIMED.height : TIMED.height);
      const large = Math.min(across, down) >= LARGE;
      return {
        ...tile,
        named,
        timed,
        large,
        wraps: large || down >= NAMED.height + SECOND_LINE + (timed ? TIMED.height : 0),
        time: saidPlaytime(tile.item.seconds, this.language()),
      };
    });
  });

  protected readonly band = computed(() => {
    const others = this.others();
    const language = this.language();
    return others
      ? {
          one: new Intl.PluralRules(language).select(others.count) === 'one',
          count: formatCount(others.count, language),
          time: saidPlaytime(others.seconds, language),
        }
      : null;
  });

  constructor() {
    const destroyed = inject(DestroyRef);
    afterNextRender(() => {
      const drawing = this.drawing()?.nativeElement;
      // A browser always has one; where there is none, the map keeps the size it assumes.
      if (!drawing || typeof ResizeObserver === 'undefined') return;
      const observer = new ResizeObserver(([entry]) => {
        const { width, height } = entry?.contentRect ?? { width: 0, height: 0 };
        if (width > 0 && height > 0) {
          this.size.set({ width: Math.round(width), height: Math.round(height) });
        }
      });
      observer.observe(drawing);
      destroyed.onDestroy(() => observer.disconnect());
    });
  }
}
