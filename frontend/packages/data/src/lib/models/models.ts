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

/**
 * What was played year by year (`GET /api/v1/library/years`): the years Steam's Year in Review
 * covers, whole, then the years from the first sync on, counted day by day.
 */
export type LibraryYears = Schemas['LibraryYearsResponse'];

/** A year of it: its time, where it was counted from, and the days it covers. */
export type LibraryYear = Schemas['YearResponse'];

/**
 * One game as the signed-in account sees it (`GET /api/v1/games/{id}`): the catalogue's record,
 * the entries it is owned through, their total, its rank among the games launched, and
 * `removed` when the account took it out of the library.
 */
export type Game = Schemas['GameResponse'];

/** A game's page in the game database (IGDB), when it knows the game. */
export type GameDataPage = Schemas['GameDataPageResponse'];

/** The sources the instance credits (`GET /api/v1/credits`). */
export type Credits = Schemas['CreditsResponse'];

/** One of them: its name as people know it, and its home page. */
export type Credit = Schemas['CreditResponse'];

/** A game's rank: its place among the games launched, the most played first, and how many those are. */
export type GameRank = Schemas['GameRankResponse'];

/** What was played of a game month by month (`GET /api/v1/games/{id}/months`), the earliest first. */
export type GameMonths = Schemas['GameMonthsResponse'];

/** A month of it: its first day, its time, and where it was counted from. */
export type GameMonth = Schemas['GameMonthResponse'];

/** The games played in one month (`GET /api/v1/library/months/{month}`), the most played first. */
export type LibraryMonth = Schemas['LibraryMonthResponse'];

/** A game of that month and its time. */
export type MonthGame = Schemas['MonthGameResponse'];

/**
 * A game the account took out of its library. `back_since` is the notice to show: a store
 * lists it again since then, and the notice was not dismissed.
 */
export type RemovedGame = Schemas['RemovedGameResponse'];

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
