import { Component, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LucideLayoutDashboard, LucideRows3 } from '@lucide/angular';
import { Pill } from '@respawn/ui';
import { HOME_LAYOUTS, type HomeLayout } from '@respawn/util';

/**
 * The home's two layouts as two pills, each drawn with its icon and named for who does not
 * see it. It shows the layout it is given and says which one was picked; where the choice is
 * kept is not its business.
 */
@Component({
  selector: 'rs-home-layout-choice',
  imports: [TranslocoDirective, LucideLayoutDashboard, LucideRows3, Pill],
  host: { role: 'group', class: 'flex gap-1', '[attr.aria-label]': 'label()' },
  templateUrl: './home-layout-choice.html',
})
export class HomeLayoutChoice {
  readonly layout = input.required<HomeLayout>();
  /** What the group is named, said before its two pills. */
  readonly label = input.required<string>();

  readonly picked = output<HomeLayout>();

  protected readonly layouts = HOME_LAYOUTS;
}
