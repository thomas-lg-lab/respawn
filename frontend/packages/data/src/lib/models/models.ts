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

/**
 * The settings of the interface kept on the account: `null` on an account that never chose,
 * where the browser's own choices apply.
 */
export type AccountSettings = Schemas['SettingsResponse'];

/** The three settings together, in place of the account's (`PUT /api/v1/me/settings`). */
export type SettingsRequest = Schemas['SettingsRequest'];

/** What the sign-in page may offer (`GET /api/v1/config`). */
export type SignInOptions = Schemas['ConfigResponse'];

/** An OpenID Connect provider to sign in with. */
export type SignInProvider = Schemas['ProviderResponse'];

export type LoginRequest = Schemas['LoginRequest'];
export type RegisterRequest = Schemas['RegisterRequest'];

/** The one error format: `{"error": {"code", "message"}}`. */
export type ApiError = Schemas['ErrorResponse'];
