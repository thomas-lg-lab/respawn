import { Component, computed, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { AccountQueries, type LoginRequest, type RegisterRequest } from '@respawn/data';
import { Banner, Button, Label, Pill, PillGroup } from '@respawn/ui';
import { injectMutation, injectQuery } from '@tanstack/angular-query';
import { failureKey } from '@app/errors/failure-message';
import { LANGUAGES, LanguageSetting } from '@app/i18n/language';
import { AccountForm } from './components/account-form';
import { SignInArt } from './components/sign-in-art';
import { SignInForm } from './components/sign-in-form';

/**
 * What the screen shows, from what the instance offers:
 * - `loading`, then `unavailable` when the instance could not say what it offers, or who
 *   is there: a visitor the API could not identify may well have a session;
 * - `local-disabled`: no local sign-in here;
 * - `first-account`: nobody has an account yet, the one created now is the administrator;
 * - `sign-in`, and `create-account` where registration is open and the visitor asked for it.
 */
type View =
  'loading' | 'unavailable' | 'local-disabled' | 'first-account' | 'sign-in' | 'create-account';

/**
 * The way in: sign in with a local account, or create one where the instance allows it.
 * This is where the forms meet the API: it asks what the instance offers, sends what a form
 * hands over, says why it was refused, and opens the app once signed in.
 *
 * The providers of the instance are not offered yet: signing in through one needs its own
 * flow in the browser, which this screen does not have.
 */
@Component({
  selector: 'rs-sign-in',
  imports: [
    TranslocoDirective,
    Banner,
    Button,
    Label,
    Pill,
    PillGroup,
    AccountForm,
    SignInArt,
    SignInForm,
  ],
  templateUrl: './sign-in.html',
})
export class SignIn {
  private readonly queries = inject(AccountQueries);
  private readonly router = inject(Router);

  protected readonly language = inject(LanguageSetting);
  protected readonly languages = LANGUAGES;
  protected readonly failureKey = failureKey;

  protected readonly options = injectQuery(() => this.queries.signInOptions());
  /**
   * Who is signed in, as the guard left it: never asked from here on its own, only read,
   * and asked again when the visitor tries again.
   */
  protected readonly account = injectQuery(() => ({ ...this.queries.me(), enabled: false }));
  protected readonly signingIn = injectMutation(() => this.queries.signIn());
  protected readonly registering = injectMutation(() => this.queries.register());

  /** The visitor asked for the account form, where registration is open. */
  private readonly wantsAccount = signal(false);

  protected readonly view = computed<View>(() => {
    const options = this.options.data();
    if (this.account.isError()) return 'unavailable';
    if (!options) return this.options.isError() ? 'unavailable' : 'loading';
    if (!options.local_sign_in) return 'local-disabled';
    if (options.first_account) return 'first-account';
    return options.registration_open && this.wantsAccount() ? 'create-account' : 'sign-in';
  });

  protected readonly creating = computed(
    () => this.view() === 'first-account' || this.view() === 'create-account',
  );

  /**
   * Why the last attempt was refused. Not tied to the form in view: a refusal can change the
   * form (registration closed meanwhile), and its reason must still be said.
   */
  protected readonly failure = computed(() => this.registering.error() ?? this.signingIn.error());

  /** Why the instance could not say who is there, or what it offers. */
  protected readonly unavailable = computed(() => this.account.error() ?? this.options.error());

  constructor() {
    // Someone is signed in: they just did it here, or trying again found their session.
    effect(() => {
      if (this.account.data()) void this.router.navigateByUrl('/');
    });
  }

  protected signIn(body: LoginRequest): void {
    this.registering.reset();
    this.signingIn.mutate(body);
  }

  protected createAccount(body: RegisterRequest): void {
    this.signingIn.reset();
    this.registering.mutate(body);
  }

  /** Asks again whatever could not be told. */
  protected retry(): void {
    if (this.account.isError()) void this.account.refetch();
    if (this.options.isError()) void this.options.refetch();
  }

  /** From one form to the other: a refusal of the first says nothing of the second. */
  protected switchForm(): void {
    this.wantsAccount.update((wants) => !wants);
    this.signingIn.reset();
    this.registering.reset();
  }
}
