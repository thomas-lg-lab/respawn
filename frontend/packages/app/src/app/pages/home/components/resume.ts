import { Component, computed, inject, input } from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucidePlay } from '@lucide/angular';
import type { LibraryGame } from '@respawn/data';
import { Button, Hero, Label } from '@respawn/ui';
import { formatAgo, formatMoment, saidPlaytime } from '@app/format/format';
import { coverHue, steamLaunchAddress } from '@app/games/game-list';
import { cameFrom, gamePage } from '@app/games/game-page';

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

  private readonly sanitizer = inject(DomSanitizer);

  /** Its title leads to its page, which leads back here. */
  protected readonly gamePage = gamePage;
  protected readonly from = cameFrom('home');

  /**
   * The link that launches the game in the Steam client. Angular takes any address that is
   * not the web's for a dangerous one and would write it off; this one is trusted because
   * `steamLaunchAddress` only ever gives `steam://run/` followed by digits.
   */
  protected readonly launch = computed(() => {
    const address = steamLaunchAddress(this.game());
    return address === null ? null : this.sanitizer.bypassSecurityTrustUrl(address);
  });

  protected readonly hue = computed(() => coverHue(this.game().game_id));
  /** The first character of the title, whole: an emoji is one, though it is two in a string. */
  protected readonly initial = computed(() =>
    ([...(this.game().name ?? '').trim()][0] ?? '').toLocaleUpperCase(this.language()),
  );

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
