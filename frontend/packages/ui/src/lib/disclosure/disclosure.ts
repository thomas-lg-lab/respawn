import { Directive, ElementRef, inject, model } from '@angular/core';

let created = 0;

/**
 * A disclosure: a button that shows and hides a panel beside it. Put on the element that
 * holds both, with `rsDisclosureTrigger` on the button and `rsDisclosurePanel` on the
 * panel:
 *
 *     <div rsDisclosure>
 *       <button rsDisclosureTrigger>…</button>
 *       <section rsDisclosurePanel>…</section>
 *     </div>
 *
 * The panel closes on Escape (the focus goes back to the button), on a click or a tap
 * outside, and when the focus leaves for another part of the page. It is not a menu: what
 * the panel holds keeps its own roles and its own tab order.
 */
@Directive({
  selector: '[rsDisclosure]',
  exportAs: 'rsDisclosure',
  host: {
    '(keydown.escape)': 'onEscape($event)',
    '(focusout)': 'onFocusOut($event)',
    '(document:click)': 'onDocumentClick($event)',
  },
})
export class Disclosure {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  readonly open = model(false);
  readonly panelId = `rs-disclosure-${++created}`;

  toggle(): void {
    this.open.update((open) => !open);
  }

  close(): void {
    this.open.set(false);
  }

  /**
   * Closes the panel and gives the focus back to the button: for what closes it from inside
   * (Escape, a link of the panel that was followed), where the focus would otherwise stay on
   * something hidden and be lost.
   */
  dismiss(): void {
    this.close();
    this.host.querySelector<HTMLElement>(`[aria-controls="${this.panelId}"]`)?.focus();
  }

  protected onEscape(event: Event): void {
    if (!this.open()) return;
    event.stopPropagation();
    this.dismiss();
  }

  protected onFocusOut(event: FocusEvent): void {
    // No next target: the window lost the focus, or a click landed on something that takes
    // none. The click itself decides then.
    const next = event.relatedTarget;
    if (next instanceof Node && !this.host.contains(next)) this.close();
  }

  protected onDocumentClick(event: Event): void {
    if (this.open() && event.target instanceof Node && !this.host.contains(event.target)) {
      this.close();
    }
  }
}

/**
 * The button of a disclosure: it says whether the panel is shown, and which one it is. Marked
 * `aria-disabled="true"`, it opens nothing and keeps the focus, where a `disabled` button would
 * drop it: the focus that came back to it from the panel stays.
 */
@Directive({
  selector: 'button[rsDisclosureTrigger]',
  host: {
    type: 'button',
    '[attr.aria-expanded]': 'disclosure.open()',
    '[attr.aria-controls]': 'disclosure.panelId',
    '(click)': 'onClick()',
  },
})
export class DisclosureTrigger {
  protected readonly disclosure = inject(Disclosure);
  private readonly host = inject<ElementRef<HTMLButtonElement>>(ElementRef).nativeElement;

  protected onClick(): void {
    if (this.host.getAttribute('aria-disabled') !== 'true') this.disclosure.toggle();
  }
}

/** The panel of a disclosure. It stays in the document while closed, hidden from everyone. */
@Directive({
  selector: '[rsDisclosurePanel]',
  host: {
    '[id]': 'disclosure.panelId',
    '[hidden]': '!disclosure.open()',
  },
})
export class DisclosurePanel {
  protected readonly disclosure = inject(Disclosure);
}
