import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Service, inject } from '@angular/core';
import { type Observable, firstValueFrom } from 'rxjs';
import type { ApiError } from '../models/models';

/** Every route of the public API lives under this prefix; the front uses no other. */
export const API_BASE = '/api/v1';

/** The code of a failure that is not an answer of the API: the request never got one. */
export const NETWORK_ERROR = 'network_error';

/** The code of an answer that is not in the API's error format: it came from something in front of it. */
export const HTTP_ERROR = 'http_error';

/**
 * A refusal: the status and the stable `code` of the API's one error format. The `message`
 * is the API's English sentence, for the console; a screen shows its own words, chosen from
 * the code.
 */
export class ApiFailure extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
  ) {
    super(message);
    this.name = 'ApiFailure';
  }
}

/** The code and the message of a body in the API's error format, or `null` for any other. */
function refusalIn(body: unknown): { code: string; message: string } | null {
  const error = (body as Partial<ApiError> | null)?.error;
  return typeof error?.code === 'string' && typeof error.message === 'string' ? error : null;
}

/** What went wrong, whatever threw: the API's refusal, or a request that got no answer. */
export function toApiFailure(thrown: unknown): ApiFailure {
  if (thrown instanceof ApiFailure) return thrown;
  if (thrown instanceof HttpErrorResponse) {
    const refusal = refusalIn(thrown.error);
    if (refusal) return new ApiFailure(thrown.status, refusal.code, refusal.message);
    // Status 0: no answer at all (offline, the server gone). Anything else that is not in
    // the error format came from something between the browser and the API.
    return new ApiFailure(
      thrown.status,
      thrown.status === 0 ? NETWORK_ERROR : HTTP_ERROR,
      thrown.message,
    );
  }
  return new ApiFailure(
    0,
    NETWORK_ERROR,
    thrown instanceof Error ? thrown.message : String(thrown),
  );
}

/**
 * The public API, as promises: what a query or a mutation function returns. A refusal is
 * always thrown as an `ApiFailure`.
 */
@Service()
export class Api {
  private readonly http = inject(HttpClient);

  get<T>(path: string): Promise<T> {
    return this.send(this.http.get<T>(API_BASE + path));
  }

  post<T>(path: string, body: unknown): Promise<T> {
    return this.send(this.http.post<T>(API_BASE + path, body));
  }

  put<T>(path: string, body: unknown): Promise<T> {
    return this.send(this.http.put<T>(API_BASE + path, body));
  }

  delete<T>(path: string): Promise<T> {
    return this.send(this.http.delete<T>(API_BASE + path));
  }

  private async send<T>(request: Observable<T>): Promise<T> {
    try {
      return await firstValueFrom(request);
    } catch (thrown) {
      throw toApiFailure(thrown);
    }
  }
}
