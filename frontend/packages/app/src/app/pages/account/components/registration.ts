import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Banner, Button, Card } from '@respawn/ui';

/**
 * Who may create an account on the instance, as its administrator sees and decides it: open
 * to anyone who reaches it, or closed.
 *
 * Opening lets anyone in, so it is asked twice: the first press says what it means, the
 * second one asks for it. Closing is one press.
 */
@Component({
  selector: 'rs-registration',
  imports: [TranslocoDirective, Banner, Button, Card],
  templateUrl: './registration.html',
})
export class Registration {
  /** Whether anyone may register now. */
  readonly open = input.required<boolean>();
  /** While the change asked for is on its way. */
  readonly busy = input(false);
  /** Why the last change was refused, as a translation key; none when it was not. */
  readonly failure = input<string | null>(null);

  /** What the administrator asks for: open, or closed. */
  readonly changed = output<boolean>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  /** The administrator asked to open, and has not confirmed yet. */
  protected readonly confirming = signal(false);

  /**
   * Asking and taking it back each replace the button pressed by another: the focus goes to
   * the one that took its place (cancel when asking: the harmless one), not to the page.
   */
  protected confirm(asked: boolean): void {
    this.confirming.set(asked);
    this.focus(asked ? '[data-registration-cancel]' : '[data-registration-change]');
  }

  protected openIt(): void {
    this.confirming.set(false);
    this.changed.emit(true);
    this.focus('[data-registration-change]');
  }

  private focus(selector: string): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }
}
