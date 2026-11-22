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

/** A provider as the signed-in account sees it: linked to it or not (`GET /api/v1/me/providers`). */
export type AccountProvider = Schemas['AccountProviderResponse'];

/** One page of the library (`GET /api/v1/library`). */
export type Library = Schemas['LibraryResponse'];

/** A game of the library, whatever the stores it is owned on: its totals add up its entries. */
export type LibraryGame = Schemas['LibraryGameResponse'];

/** A game as one linked account owns it on one store. */
export type LibraryEntry = Schemas['LibraryEntryResponse'];

/** The figures of the library (`GET /api/v1/library/stats`). */
export type LibraryStats = Schemas['LibraryStatsResponse'];

/**
 * What was played day by day since the first sync (`GET /api/v1/library/history`). A day's
 * time is what the reading of that day found more than the one before; days are in UTC.
 */
export type LibraryHistory = Schemas['LibraryHistoryResponse'];

/** A day something was played, and the games it was. */
export type HistoryDay = Schemas['HistoryDayResponse'];

/** The linked store accounts (`GET /api/v1/linked-accounts`). */
export type LinkedAccounts = Schemas['LinkedAccountsResponse'];

/** A store account linked to the signed-in account, with the state of its sync. */
export type LinkedAccount = Schemas['LinkedAccountResponse'];

/** Where the sync of a linked account stands: running or not, its last run, its last success. */
export type SyncState = Schemas['SyncStateResponse'];

/**
 * One reading of a linked account's library, finished. `outcome` is `succeeded`,
 * `nothing_visible` (the store showed no game: a private profile looks the same) or `failed`,
 * and then `error` says why: `not_configured`, `refused`, `unavailable` or `unreadable`.
 */
export type SyncRun = Schemas['SyncRunResponse'];

export type LoginRequest = Schemas['LoginRequest'];
export type RegisterRequest = Schemas['RegisterRequest'];

/** The one error format: `{"error": {"code", "message"}}`. */
export type ApiError = Schemas['ErrorResponse'];
