import {
  Component,
  DestroyRef,
  type ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Card } from '@respawn/ui';
import { mixed, treemap } from '@app/games/time-map';
import type { MapGame } from '../library-blocks';

/** The drawing's width over its height until it is measured: about what a phone gives it. */
const RATIO = 2.6;

/**
 * The time map at a glance: the most played games as tiles, each as large as its playtime
 * and coloured by its last session, scattered over the drawing (`mixed`). A drawing only, with what it shows said under it: no
 * tile names its game, and the ranking names the first of them only. The whole card leads to
 * the Time tab, where the map is drawn in full.
 *
 * The drawing keeps its height and takes the width it is given, so its shape changes with
 * the window: it is measured, and the tiles are cut for the shape it has.
 */
@Component({
  selector: 'rs-time-map-preview',
  imports: [RouterLink, TranslocoDirective, Card],
  templateUrl: './time-map-preview.html',
})
export class TimeMapPreview {
  readonly games = input.required<readonly MapGame[]>();

  private readonly drawing = viewChild<ElementRef<HTMLElement>>('drawing');
  /** To one decimal: a pixel more or less does not cut the map again. */
  private readonly ratio = signal(RATIO);

  protected readonly tiles = computed(() =>
    treemap(
      mixed(this.games(), (game) => game.id),
      (game) => game.seconds,
      this.ratio(),
    ),
  );

  constructor() {
    const destroyed = inject(DestroyRef);
    afterNextRender(() => {
      const drawing = this.drawing()?.nativeElement;
      // A browser always has one; where there is none, the map keeps the shape it assumes.
      if (!drawing || typeof ResizeObserver === 'undefined') return;
      const observer = new ResizeObserver(([entry]) => {
        const { width, height } = entry?.contentRect ?? { width: 0, height: 0 };
        if (width > 0 && height > 0) this.ratio.set(Math.round((width / height) * 10) / 10);
      });
      observer.observe(drawing);
      destroyed.onDestroy(() => observer.disconnect());
    });
  }
}
