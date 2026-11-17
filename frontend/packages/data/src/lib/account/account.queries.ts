import { Service, inject } from '@angular/core';
import { QueryClient, hashKey, mutationOptions, queryOptions } from '@tanstack/angular-query';
import { Api, ApiFailure } from '../api/api';
import type { Account, LoginRequest, RegisterRequest, SignInOptions } from '../models/models';

/** The keys of what the cache holds about the account, so each is named in one place. */
export const accountKeys = {
  me: ['account', 'me'] as const,
  signInOptions: ['account', 'sign-in-options'] as const,
};

/** The refusals that mean the sign-in page offered what the instance no longer does. */
const NO_LONGER_OFFERED = ['registration_closed', 'local_sign_in_disabled'];

/**
 * The server state of the account: who is signed in, what the sign-in page may offer, and
 * the three actions that change it. Each method gives the options of a query or of a
 * mutation; a component passes them to `injectQuery` or `injectMutation`, a guard to the
 * `QueryClient`.
 *
 * The cache is the session as the front knows it: signing in writes the account into it,
 * signing out empties it, so no screen waits for a second request to learn the outcome.
 */
@Service()
export class AccountQueries {
  private readonly api = inject(Api);
  private readonly client = inject(QueryClient);

  /**
   * The signed-in account, or `null`: "nobody is signed in" is an answer here, not a
   * failure. Kept until a sign-in or a sign-out changes it.
   */
  me() {
    return queryOptions({
      queryKey: accountKeys.me,
      queryFn: async (): Promise<Account | null> => {
        try {
          return await this.api.get<Account>('/me');
        } catch (failure) {
          if (failure instanceof ApiFailure && failure.status === 401) return null;
          throw failure;
        }
      },
      staleTime: Infinity,
      retry: false,
    });
  }

  /** What the sign-in page may offer. Asked again on each visit: an account may have been created since. */
  signInOptions() {
    return queryOptions({
      queryKey: accountKeys.signInOptions,
      queryFn: () => this.api.get<SignInOptions>('/config'),
      retry: false,
    });
  }

  signIn() {
    return mutationOptions({
      mutationFn: (body: LoginRequest) => this.api.post<Account>('/auth/login', body),
      onSuccess: (account) => this.signedIn(account),
      onError: (failure) => this.refused(failure),
    });
  }

  /** Creates a local account, which is signed in at once. */
  register() {
    return mutationOptions({
      mutationFn: (body: RegisterRequest) => this.api.post<Account>('/auth/register', body),
      onSuccess: (account) => this.signedIn(account),
      onError: (failure) => this.refused(failure),
    });
  }

  signOut() {
    return mutationOptions({
      mutationFn: () => this.api.post<void>('/auth/logout', {}),
      onSuccess: () => {
        // Nothing read for the account that left stays behind for the next one.
        this.client.removeQueries({
          predicate: (query) => query.queryHash !== hashKey(accountKeys.me),
        });
        this.client.setQueryData(accountKeys.me, null);
      },
    });
  }

  private signedIn(account: Account): void {
    this.client.setQueryData(accountKeys.me, account);
    // The first account closes registration: what the page offered is not kept for a later visit.
    this.client.removeQueries({ queryKey: accountKeys.signInOptions });
  }

  /**
   * What the page offered changed since it asked (the first account was created meanwhile,
   * say): it asks again, so the screen shows what the instance offers now.
   */
  private refused(failure: unknown): void {
    if (failure instanceof ApiFailure && NO_LONGER_OFFERED.includes(failure.code)) {
      void this.client.invalidateQueries({ queryKey: accountKeys.signInOptions });
    }
  }
}
