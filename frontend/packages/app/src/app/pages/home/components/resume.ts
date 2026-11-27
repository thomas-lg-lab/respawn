import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucidePlay } from '@lucide/angular';
import type { LibraryGame } from '@respawn/data';
import { Button, Hero, Label } from '@respawn/ui';
import { formatAgo, formatMoment, saidPlaytime } from '@app/format/format';
import { coverHue, titleInitial } from '@app/games/game-list';
import { cameFrom, gamePage } from '@app/games/game-page';
import { steamLaunchLink } from '@app/games/steam-launch';

/**
 * The game to resume, the one played last, as a banner: its title, how long ago its last
 * session was, its playtime in all and over the last two weeks, and the way to launch it in
 * Steam when a copy of it is there, on a computer (a phone has no Steam client to ask). What
 * the store did not say is left out, never shown as zero. Its title leads to its page.
 */
@Component({
  selector: 'rs-resume',
  imports: [RouterLink, TranslocoDirective, LucidePlay, Button, Hero, Label],
  templateUrl: './resume.html',
})
export class Resume {
  readonly game = input.required<LibraryGame>();
  readonly language = input.required<string>();
  /** The moment "ago" is counted from. */
  readonly now = input.required<number>();

  /** Its title leads to its page, which leads back here. */
  protected readonly gamePage = gamePage;
  protected readonly from = cameFrom('home');

  /** The link that launches the game in the Steam client, where a copy of it is listed. */
  protected readonly launch = steamLaunchLink(this.game);

  protected readonly hue = computed(() => coverHue(this.game().game_id));
  protected readonly initial = computed(() => titleInitial(this.game().name, this.language()));

  /** The last session, as it reads inside a sentence: "yesterday", "3 days ago". */
  protected readonly last = computed(() => {
    const moment = this.game().last_played_at;
    if (!moment) return null;
    const ago = formatAgo(moment, this.now(), this.language());
    return {
      ago: ago.charAt(0).toLocaleLowerCase(this.language()) + ago.slice(1),
      moment: formatMoment(moment, this.language()),
    };
  });

  protected readonly total = computed(() => this.said(this.game().playtime_seconds));
  /** No time over the two weeks is not worth a word here: the last session says it. */
  protected readonly recent = computed(() =>
    this.said(this.game().recent_playtime_seconds || null),
  );

  private said(seconds: number | null) {
    return seconds === null ? null : saidPlaytime(seconds, this.language());
  }
}
