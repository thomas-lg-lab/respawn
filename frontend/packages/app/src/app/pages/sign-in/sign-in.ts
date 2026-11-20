import { Location } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  AccountQueries,
  type LoginRequest,
  PROVIDER_OUTCOME,
  type RegisterRequest,
  providerOutcome,
  providerSignInAddress,
} from '@respawn/data';
import { Banner, Button, Label, Pill, PillGroup } from '@respawn/ui';
import { injectMutation, injectQuery } from '@tanstack/angular-query';
import { failureKey } from '@app/errors/failure-message';
import { LANGUAGES, LanguageSetting } from '@app/i18n/language';
import { RETURN_TO, returnAddress, signInFrom } from '@app/session/return-address';
import { saidOnce } from '@app/session/said-once';
import { AccountForm } from './components/account-form';
import { type ProviderChoice, ProviderChoices } from './components/provider-choices';
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
 * The way in: sign in with a local account, or create one where the instance allows it, or
 * through one of the instance's providers. This is where the forms meet the API: it asks what
 * the instance offers, sends what a form hands over, says why it was refused, and opens the
 * app once signed in.
 *
 * A provider is a navigation: the API sends the browser there and back to this screen, which
 * opens the app when a session came of it (its guard does) and says why otherwise.
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
    ProviderChoices,
    SignInArt,
    SignInForm,
  ],
  templateUrl: './sign-in.html',
})
export class SignIn {
  private readonly queries = inject(AccountQueries);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

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
    // A new try for who is there: no form, and no button to press twice, until it answers.
    if (this.account.isFetching()) return 'loading';
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

  /** The providers of the instance, each with the address that starts a sign-in through it. */
  protected readonly providers = computed<ProviderChoice[]>(() => {
    const back = this.router.serializeUrl(
      signInFrom(this.router, this.route.snapshot.queryParamMap.get(RETURN_TO) ?? '/'),
    );
    return (this.options.data()?.oidc_providers ?? []).map((provider) => ({
      ...provider,
      address: providerSignInAddress(provider.name, back),
    }));
  });

  /**
   * What came of a sign-in through a provider that did not open a session, as the API wrote it
   * on the address it sent the browser back to. Said until the visitor tries again, and once:
   * the word is taken off the browser's address.
   */
  protected readonly cameBack = providerOutcome(
    this.route.snapshot.queryParamMap.get(PROVIDER_OUTCOME),
  );

  constructor() {
    saidOnce(inject(Location), this.router, PROVIDER_OUTCOME);
  }

  protected signIn(body: LoginRequest): void {
    this.registering.reset();
    this.signingIn.mutate(body, { onSuccess: () => this.enter() });
  }

  protected createAccount(body: RegisterRequest): void {
    this.signingIn.reset();
    this.registering.mutate(body, { onSuccess: () => this.enter() });
  }

  /** Asks again whatever could not be told; a session found that way opens the app. */
  protected async retry(): Promise<void> {
    if (this.options.isError()) void this.options.refetch();
    if (this.account.isError() && (await this.account.refetch()).data) this.enter();
  }

  /** From one form to the other: a refusal of the first says nothing of the second. */
  protected switchForm(): void {
    this.wantsAccount.update((wants) => !wants);
    this.signingIn.reset();
    this.registering.reset();
  }

  /** Into the app: where the visitor was headed when they were sent here, else the home. */
  private enter(): void {
    const asked = this.route.snapshot.queryParamMap.get(RETURN_TO);
    void this.router.navigateByUrl(returnAddress(asked));
  }
}
