import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import type { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query';
import { serverState } from '../server-state';

/**
 * For the specs of a feature's queries, in their `beforeEach`: the feature's service on the
 * real server state, the API played by `HttpTestingController`. The spec checks in its
 * `afterEach` that no request was left unanswered (`http.verify()`).
 */
export function setUpQueries<T>(service: Type<T>) {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideTanStackQuery(() => serverState()),
    ],
  });
  const http = TestBed.inject(HttpTestingController);
  return {
    queries: TestBed.inject(service),
    client: TestBed.inject(QueryClient),
    http,
    /** The request once it left: a query starts on a later tick. */
    request: (url: string) => vi.waitFor(() => http.expectOne(url)),
  };
}

/** What `setUpQueries` gives: a spec keeps each part in a variable of its own. */
export type QueriesSetUp<T> = ReturnType<typeof setUpQueries<T>>;
