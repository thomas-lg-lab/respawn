import { Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Card } from '@respawn/ui';
import type { MapTile } from '../library-blocks';

/**
 * The time map at a glance: the most played games as tiles, each as large as its playtime
 * and coloured by its last session. A drawing only, with what it shows said under it; the
 * games are named by the ranking beside it.
 */
@Component({
  selector: 'rs-time-map-preview',
  imports: [TranslocoDirective, Card],
  templateUrl: './time-map-preview.html',
})
export class TimeMapPreview {
  readonly tiles = input.required<readonly MapTile[]>();
}
