import { Component, inject } from '@angular/core';
import { AccountQueries } from '@respawn/data';
import { injectQuery } from '@tanstack/angular-query';
import { AccountSummary } from './components/account-summary';

/**
 * The first screen after sign-in. For now it answers one question, "who am I": the library
 * takes this place when it has a screen.
 */
@Component({
  selector: 'rs-home',
  imports: [AccountSummary],
  templateUrl: './home.html',
})
export class Home {
  private readonly queries = inject(AccountQueries);

  protected readonly me = injectQuery(() => this.queries.me());
}
