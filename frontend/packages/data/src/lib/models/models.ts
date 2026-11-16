/**
 * The models of the public API, under the names the front uses.
 *
 * Their shapes are not written here: they come from `api-types.ts`, which
 * `npm run gen:api` generates from `openapi.json`, the API's own description. A change of
 * the API that is regenerated changes these types with it, and what no longer fits fails to
 * compile. Fields are in snake_case, as the API sends them.
 */
import type { components } from './api-types';

type Schemas = components['schemas'];

/** The signed-in account (`GET /api/v1/me`, and the answer to a sign-in). */
export type Account = Schemas['UserResponse'];

/** What the sign-in page may offer (`GET /api/v1/config`). */
export type SignInOptions = Schemas['ConfigResponse'];

/** An OpenID Connect provider to sign in with. */
export type SignInProvider = Schemas['ProviderResponse'];

export type LoginRequest = Schemas['LoginRequest'];
export type RegisterRequest = Schemas['RegisterRequest'];
export type ProviderSignInRequest = Schemas['OidcRequest'];

/** The one error format: `{"error": {"code", "message"}}`. */
export type ApiError = Schemas['ErrorResponse'];
