import { Component, output, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Button, SearchField } from '@respawn/ui';

/**
 * The search of the home: a title, which the Library then looks for. It only says what was
 * typed, once asked; where that leads is the screen's.
 */
@Component({
  selector: 'rs-game-search',
  imports: [TranslocoDirective, Button, SearchField],
  templateUrl: './game-search.html',
})
export class GameSearch {
  readonly searched = output<string>();

  protected readonly typed = signal('');

  protected submit(event: Event): void {
    // The form is only the way to ask with the Enter key: nothing is sent.
    event.preventDefault();
    this.searched.emit(this.typed().trim());
  }
}
