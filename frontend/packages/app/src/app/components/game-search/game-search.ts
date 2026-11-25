import {
  afterNextRender,
  Component,
  computed,
  ElementRef,
  inject,
  Injector,
  input,
  linkedSignal,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideSearch, LucideX } from '@lucide/angular';
import type { LibraryGame } from '@respawn/data';
import { Cover, SearchField } from '@respawn/ui';
import { formatCount, isOne, type SaidPlaytime, saidPlaytime } from '@app/format/format';
import { coverHue, type FoundTitle, foundIn, suggested } from '@app/games/game-list';

/** How many letters before games are suggested: one alone names half of a library. */
const SUGGESTS_FROM = 2;
/** How many games are suggested: the others are a search of the Library away. */
const SUGGESTED = 5;

let created = 0;

/** A game suggested under the field: its title cut around what was typed, and its playtime. */
interface Suggestion {
  readonly id: string;
  readonly title: string;
  readonly cut: FoundTitle;
  readonly hue: number;
  /** `never` for a game no store says was played, `unknown` when no store said. */
  readonly time: SaidPlaytime | 'never' | 'unknown';
}

/**
 * The search of the header: a title, and from two letters the games of the library that hold
 * it, those that start with it first. A suggestion, or the Enter key, asks for a title; where that
 * leads is the shell's. On a phone it is a button, and the field takes the header once asked
 * for. Ctrl+K (⌘K) brings the focus to it from anywhere.
 *
 * It only shows the games it is given: the shell reads them once the field is first used.
 */
@Component({
  selector: 'rs-game-search',
  imports: [TranslocoDirective, Cover, SearchField, LucideSearch, LucideX],
  host: { '(document:keydown)': 'shortcut($event)' },
  templateUrl: './game-search.html',
})
export class GameSearch {
  /** The games of the library; none while they are not read yet. */
  readonly games = input<readonly LibraryGame[]>();
  readonly language = input.required<string>();

  /**
   * A title was asked for: what was typed, or the title of a suggestion.
   *
   * Until a game has a page of its own (iteration 1). From then on a suggestion says its game
   * through an output of its own, and the shell leads to that game's page; Enter with no
   * suggestion chosen and the foot of the list still lead to the Library searched.
   */
  readonly searched = output<string>();
  /** The field took the focus: the games will be needed. */
  readonly opened = output<void>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly injector = inject(Injector);
  private readonly field = viewChild.required(SearchField);

  protected readonly listId = `rs-game-search-${++created}`;
  protected readonly typed = signal('');
  /** The focus is in the search: what it suggests is shown. */
  private readonly focused = signal(false);
  /** On a phone, the field was asked for and stands over the header. */
  protected readonly unfolded = signal(false);

  protected readonly asked = computed(() => this.typed().trim());

  /** The games that hold what was typed, the closest first; `null` while none is suggested. */
  private readonly found = computed(() => {
    const games = this.games();
    const asked = this.asked();
    if (!games || asked.length < SUGGESTS_FROM) return null;
    return suggested(games, asked, this.language());
  });

  protected readonly suggestions = computed<Suggestion[]>(() =>
    (this.found() ?? []).slice(0, SUGGESTED).map((game, index) => {
      const title = game.name ?? '';
      return {
        id: `${this.listId}-${index}`,
        title,
        cut: foundIn(title, this.asked()) ?? { before: title, found: '', after: '' },
        hue: coverHue(game.game_id),
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

  /** The suggestion the arrow keys are on: none until one is pressed, and after each letter. */
  protected readonly active = linkedSignal({ source: this.suggestions, computation: () => -1 });
  protected readonly activeId = computed(() => this.suggestions()[this.active()]?.id);

  protected entered(): void {
    if (this.focused()) return;
    this.focused.set(true);
    this.opened.emit();
  }

  /** The focus left for another part of the page, or for nothing: the search closes. */
  protected left(event: FocusEvent): void {
    const next = event.relatedTarget;
    if (next instanceof Node && this.host.contains(next)) return;
    this.focused.set(false);
    if (!this.asked()) this.unfolded.set(false);
  }

  protected key(event: KeyboardEvent): void {
    const count = this.suggestions().length;
    switch (event.key) {
      case 'ArrowDown':
        if (!count) return;
        this.active.update((active) => (active + 1) % count);
        break;
      case 'ArrowUp':
        if (!count) return;
        this.active.update((active) => (active <= 0 ? count : active) - 1);
        break;
      case 'Enter': {
        // Enter on a button of the search is that button's own click.
        if (event.target instanceof HTMLButtonElement) return;
        const title = this.suggestions()[this.active()]?.title ?? this.asked();
        if (!title) return;
        this.ask(title);
        break;
      }
      case 'Escape':
        // What was typed goes first, the focus staying for another title; then the search itself.
        if (this.typed()) this.typed.set('');
        else this.close();
        break;
      default:
        return;
    }
    event.preventDefault();
  }

  /** Ctrl+K, or ⌘K: the focus goes to the field, from anywhere in the app. */
  protected shortcut(event: KeyboardEvent): void {
    if (!(event.ctrlKey || event.metaKey) || event.altKey || event.key.toLowerCase() !== 'k') {
      return;
    }
    event.preventDefault();
    this.unfold();
  }

  /** Shows the field, on a phone too, and gives it the focus once it is drawn. */
  protected unfold(): void {
    this.unfolded.set(true);
    afterNextRender(() => this.field().focus(), { injector: this.injector });
  }

  protected clear(): void {
    this.typed.set('');
    this.field().focus();
  }

  protected ask(title: string): void {
    this.searched.emit(title);
    this.close();
  }

  /** Empties the search and lets go of the focus: nothing is suggested any more. */
  protected close(): void {
    this.typed.set('');
    this.unfolded.set(false);
    this.focused.set(false);
    const focus = document.activeElement;
    if (focus instanceof HTMLElement && this.host.contains(focus)) focus.blur();
  }
}
