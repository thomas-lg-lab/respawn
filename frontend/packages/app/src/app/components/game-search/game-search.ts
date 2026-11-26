import {
  ChangeDetectorRef,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  linkedSignal,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideSearch, LucideX } from '@lucide/angular';
import { type LibraryGame, coverAddress } from '@respawn/data';
import { Button, Cover, SearchField } from '@respawn/ui';
import { formatCount, isOne, type SaidPlaytime, saidPlaytime } from '@app/format/format';
import { coverHue, type FoundTitle, foundIn, letters, suggested } from '@app/games/game-list';

/** How many letters before games are suggested: one alone names half of a library. */
const SUGGESTS_FROM = 2;
/** How many games are suggested: the others are a search of the Library away. */
const SUGGESTED = 5;

/** The key that goes with K: Command on an Apple machine, where Ctrl+K edits the line. */
const APPLE = /Mac|iPhone|iPad/.test(navigator.platform);

let created = 0;

/** A game suggested under the field: its title cut around what was typed, and its playtime. */
interface Suggestion {
  readonly id: string;
  readonly gameId: string;
  readonly title: string;
  readonly cut: FoundTitle;
  readonly hue: number;
  readonly cover: string;
  /** `never` for a game no store says was played, `unknown` when no store said. */
  readonly time: SaidPlaytime | 'never' | 'unknown';
}

/**
 * The search of the header: a title, and from two letters the games of the library that hold
 * it, those that start with it first. A suggestion chosen names its game; the Enter key with
 * none chosen, or the foot of the list, asks for a title. Where each leads is the shell's. Where the header has no room for a field it is a button, and the field
 * unfolds over the header once asked for, then folds back when the search is left or done. Ctrl+K (⌘K on an Apple machine) brings the focus to it from anywhere.
 *
 * It only shows the games it is given: the shell reads them once the field is first used.
 */
@Component({
  selector: 'rs-game-search',
  imports: [TranslocoDirective, Button, Cover, SearchField, LucideSearch, LucideX],
  host: { '(document:keydown)': 'shortcut($event)', '(document:click)': 'pressedOutside($event)' },
  templateUrl: './game-search.html',
})
export class GameSearch {
  /** The games of the library; none while they are not read yet. */
  readonly games = input<readonly LibraryGame[]>();
  /** The games could not be read: said under the field, since nothing can be suggested. */
  readonly unreadable = input(false);
  readonly language = input.required<string>();

  /**
   * A title was asked for: what was typed, by Enter with no suggestion chosen or by the foot
   * of the list. The shell leads to the Library searched.
   */
  readonly searched = output<string>();
  /** A suggestion was chosen: the id of its game. The shell leads to the game's page. */
  readonly chosen = output<string>();
  /** The field itself took the focus: the games will be needed. */
  readonly opened = output<void>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly changes = inject(ChangeDetectorRef);
  private readonly field = viewChild.required(SearchField);
  private readonly opener = viewChild.required<ElementRef<HTMLButtonElement>>('opener');

  protected readonly apple = APPLE;

  protected readonly listId = `rs-game-search-${++created}`;
  protected readonly typed = signal('');
  /** The focus is in the search: what it suggests is shown. */
  private readonly focused = signal(false);
  /** Where the search is a button, the field was asked for and stands over the header. */
  protected readonly unfolded = signal(false);

  protected readonly asked = computed(() => this.typed().trim());

  /** The games that hold what was typed, the closest first; `null` while none is suggested. */
  private readonly found = computed(() => {
    const games = this.games();
    const asked = this.asked();
    if (!games || letters(asked) < SUGGESTS_FROM) return null;
    return suggested(games, asked, this.language());
  });

  protected readonly suggestions = computed<Suggestion[]>(() =>
    (this.found() ?? []).slice(0, SUGGESTED).map((game, index) => {
      const title = game.name ?? '';
      return {
        id: `${this.listId}-${index}`,
        gameId: game.game_id,
        title,
        cut: foundIn(title, this.asked()) ?? { before: title, found: '', after: '' },
        hue: coverHue(game.game_id),
        cover: coverAddress(game.game_id),
        time:
          game.playtime_seconds === null
            ? 'unknown'
            : game.playtime_seconds > 0
              ? saidPlaytime(game.playtime_seconds, this.language())
              : 'never',
      };
    }),
  );

  /** How many games the Library would show for what was typed, and whether that is one. */
  protected readonly all = computed(() => {
    const count = this.found()?.length ?? 0;
    return { one: isOne(count, this.language()), count: formatCount(count, this.language()) };
  });

  protected readonly shown = computed(() => this.focused() && this.found() !== null);
  /**
   * Enough was typed for suggestions, and the games they would come from could not be read.
   * Not while games read earlier are still held: a reading that failed since takes nothing
   * away from what can be suggested.
   */
  protected readonly failed = computed(
    () =>
      this.focused() &&
      this.unreadable() &&
      !this.games() &&
      letters(this.asked()) >= SUGGESTS_FROM,
  );
  /** A list of suggestions is shown: there is none to show when no game was found. */
  protected readonly listed = computed(() => this.shown() && this.suggestions().length > 0);

  /**
   * The suggestion the arrow keys are on: none until one is pressed, after each letter, and
   * once the focus left.
   */
  protected readonly active = linkedSignal({ source: this.suggestions, computation: () => -1 });
  protected readonly activeId = computed(() =>
    this.listed() ? this.suggestions()[this.active()]?.id : undefined,
  );

  /**
   * The focus came into the field or what it suggests; the games are only needed once it is in
   * the field. On the button that unfolds the field, the search is still closed.
   */
  protected entered(event: FocusEvent): void {
    if (event.target === this.opener().nativeElement) return;
    this.focused.set(true);
    if (event.target instanceof HTMLInputElement) this.opened.emit();
  }

  /**
   * The focus left for another part of the page: the search closes. Not when it left for
   * nothing: the window lost the focus, a phone put its keyboard away, or a press landed on
   * something that takes none. The search stays as it was, and a press outside closes it.
   */
  protected left(event: FocusEvent): void {
    const next = event.relatedTarget;
    if (next instanceof Node && !this.host.contains(next)) this.shut();
  }

  /** A press outside the search closes it, wherever the focus is. */
  protected pressedOutside(event: Event): void {
    if (!this.focused() && !this.unfolded()) return;
    // The path the press took: what it landed on may have left the page since.
    if (!event.composedPath().includes(this.host)) this.shut();
  }

  /** Nothing is suggested any more, and the field folds back; what was typed stays. */
  private shut(): void {
    this.focused.set(false);
    this.active.set(-1);
    this.unfolded.set(false);
  }

  protected key(event: KeyboardEvent): void {
    // A text being composed keeps its keys: Enter picks a candidate, Escape gives it up.
    if (event.isComposing || event.keyCode === 229) return;
    // The button that unfolds the field has its own: the search is closed while it has the focus.
    if (event.target === this.opener().nativeElement) return;

    const count = this.suggestions().length;
    switch (event.key) {
      case 'ArrowDown':
        if (!count) return;
        // Past the last one, back to what was typed: no suggestion chosen.
        this.active.update((active) => (active + 1 < count ? active + 1 : -1));
        break;
      case 'ArrowUp':
        if (!count) return;
        this.active.update((active) => (active < 0 ? count : active) - 1);
        break;
      case 'Enter': {
        // Enter on a button of the search is that button's own click.
        if (event.target instanceof HTMLButtonElement) return;
        const suggestion = this.suggestions()[this.active()];
        if (suggestion) {
          this.choose(suggestion.gameId);
          break;
        }
        // Nothing the search reads was typed: nothing to look for.
        if (!letters(this.asked())) return;
        this.ask(this.asked());
        break;
      }
      case 'Escape':
        // What was typed goes first, the focus in the field for another title (the button it
        // was on may leave with what was typed); then the search itself.
        if (this.typed()) this.clear();
        else this.close();
        break;
      default:
        return;
    }
    event.preventDefault();
  }

  /**
   * Ctrl+K, or ⌘K on an Apple machine: the focus goes to the field, from anywhere in the app.
   * That key alone with K, so the browser's and the system's other shortcuts stay theirs. K
   * is the letter the key writes; where the layout writes no Latin letter with it (Cyrillic,
   * Greek), it is the key where K is on a Latin keyboard.
   */
  protected shortcut(event: KeyboardEvent): void {
    const held = APPLE ? event.metaKey && !event.ctrlKey : event.ctrlKey && !event.metaKey;
    if (!held || event.altKey || event.shiftKey) return;
    const letter = /^[a-z]$/i.test(event.key) ? event.key.toLowerCase() : null;
    if (letter === null ? event.code !== 'KeyK' : letter !== 'k') return;

    event.preventDefault();
    this.unfold();
  }

  /**
   * Shows the field, on a phone too, and gives it the focus. The field is drawn at once
   * rather than at the next render: a phone only brings its keyboard up for a focus given
   * while the press is still being handled.
   */
  protected unfold(): void {
    this.unfolded.set(true);
    this.changes.detectChanges();
    this.field().focus();
  }

  /** A press on the bar's own margin keeps the focus where it is: it asks for nothing. */
  protected pressed(event: MouseEvent): void {
    if (event.target === event.currentTarget) event.preventDefault();
  }

  protected clear(): void {
    this.typed.set('');
    this.field().focus();
  }

  protected ask(title: string): void {
    this.searched.emit(title);
    this.close();
  }

  protected choose(gameId: string): void {
    this.chosen.emit(gameId);
    this.close();
  }

  /**
   * Empties the search and lets go of the focus: nothing is suggested any more. On a phone the
   * focus goes back to the button that opened the field, drawn again first; where there is no
   * such button to see, it is let go.
   */
  protected close(): void {
    this.typed.set('');
    this.shut();
    this.changes.detectChanges();

    this.opener().nativeElement.focus();
    const focus = document.activeElement;
    if (focus === this.opener().nativeElement) return;
    if (focus instanceof HTMLElement && this.host.contains(focus)) focus.blur();
  }
}
