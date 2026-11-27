<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/logo-dark.svg">
  <img src="docs/logo-light.svg" alt="" width="72" height="72">
</picture>

# respawn

[![CI](https://github.com/thomas-lg-lab/respawn/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/thomas-lg-lab/respawn/actions/workflows/ci.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/thomas-lg-lab/respawn/badge)](https://scorecard.dev/viewer/?uri=github.com/thomas-lg-lab/respawn)
[![License: AGPL-3.0-or-later](https://img.shields.io/badge/license-AGPL--3.0--or--later-blue)](LICENSE)
[![.NET 11](https://img.shields.io/badge/.NET-11-512BD4)](global.json)
[![PostgreSQL 18](https://img.shields.io/badge/PostgreSQL-18-4169E1)](docker-compose.yml)
[![Angular 22](https://img.shields.io/badge/Angular-22-DD0031)](frontend/package.json)
[![Tailwind CSS 4](https://img.shields.io/badge/Tailwind%20CSS-4-06B6D4)](frontend/package.json)
[![Conventional Commits](https://img.shields.io/badge/Conventional%20Commits-1.0.0-FE5196)](https://www.conventionalcommits.org)

Self-hosted game library: stats, backlog and next-game suggestions.

Link a Steam account and respawn reads its library every day: what you own, how long you played each game,
and, from one day's reading to the next, what you played and when. The home tells you what to pick up again
and where your time went.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/home-dark.webp">
  <img src="docs/screenshots/home-light.webp" alt="The home: the game to resume, the games played lately, three figures, the time played per year and the most played games">
</picture>

<table>
  <tr>
    <td width="50%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/library-dark.webp">
        <img src="docs/screenshots/library-light.webp" alt="The library as covers, each with its title and its playtime, and the choice of covers or a list beside the sort">
      </picture>
    </td>
    <td width="50%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="docs/screenshots/time-dark.webp">
        <img src="docs/screenshots/time-light.webp" alt="The Time tab: the most played games as tiles as large as their playtime, coloured by their last session">
      </picture>
    </td>
  </tr>
  <tr>
    <td align="center"><b>The library</b>: covers or a list; search, filter, sort</td>
    <td align="center"><b>The time map</b>: where the hours went</td>
  </tr>
</table>

<sub>A demo library: real titles, made-up playtime.</sub>

**Status: early days.** What works today:

- **Your Steam library, read every day** by the app itself, with the instance's Steam Web API key or one you
  give (kept encrypted, never shown). Each reading is kept, and the history is built from them: no game is
  ever taken out behind your back, one the store stops listing is only flagged. The years before the first
  reading come from Steam's Year in Review, from 2022 on.
- **The home**: the game to resume, with "Launch in Steam", the ones played lately, your playtime, the last
  two weeks and the games never launched, the time played per year, the most played and their time map,
  three never launched drawn at random, and the latest days played.
- **The library** as covers or as a list, each browser keeping its own choice, searched, filtered (launched,
  recent, never launched) and sorted; **the Time tab**, the time map in full.
- **Steam's covers** wherever a game is shown, asked of Steam once and kept by the instance when it has a
  data directory; a cover is asked for only as it comes near the screen.
- **The page of a game**, reached from wherever a game is shown and from the search: its time, its rank, this
  year and the last two weeks, its months since 2022 as a calendar, and for a month the other games played
  then; "Launch in Steam" on a computer, and taking a game Steam no longer lists out of the library.
- **Accounts**: local accounts (the first one is the administrator), or sign-in through any OpenID Connect
  provider; the language (French or English), the theme and the character of the labels kept on the
  account; the account screen with its linked Steam accounts, a reading on request, your own key and
  unlinking.
- **A versioned public API** under `/api/v1` with its OpenAPI description, which the front uses and nothing
  else.
- On a phone as on a desktop, in light or dark.

**Roadmap**, in order:

1. **Next**: genres, descriptions and durations through IGDB, and with them the backlog in hours; a first
   try at searching your own backlog by what you feel like playing. What IGDB says of each game is now
   kept on the server (below); nothing shows it yet.
2. **Statuses and ratings** for each game.
3. **Tokens for the public API.**
4. **More stores, next-game suggestions, sharing with a group.**

What changed lately: the [commits on `main`](https://github.com/thomas-lg-lab/respawn/commits/main), one per
pull request. No release is published yet; each one will list its pull requests in its notes.

Contents: [Run it](#run-it) · [Configuration](#configuration) · [Behind a reverse proxy](#behind-a-reverse-proxy) ·
[Sign-in](#sign-in) · [API](#api) · [How it is built](#how-it-is-built) ·
[Images and releases](#images-and-releases) · [Development](#development) · [Security](#security) ·
[License](#license)

## Run it

With Docker, the app and its database:

```bash
docker compose up --build
```

Then open http://localhost:8080. The first account created becomes the administrator; registration is closed
after it.

The image alone, against a PostgreSQL database of yours:

```bash
docker run --rm -p 8080:8080 --read-only --tmpfs /tmp \
  -v respawn-data:/data -e RESPAWN_DATA_DIR=/data \
  -e RESPAWN_DATABASE_URL=postgresql://respawn:change-me@db.example.org:5432/respawn \
  ghcr.io/thomas-lg-lab/respawn:edge
```

What the app expects from where it runs:

| | |
| --- | --- |
| HTTP | port 8080; `GET /healthz` answers `{"status": "ok"}` with no database and no secret |
| Database | PostgreSQL (tested on 18); the app creates and migrates its own schemas on the first connection |
| Filesystem | read-only root filesystem; nothing is written outside `/tmp` and, when `RESPAWN_DATA_DIR` is set, that directory (the covers; `/data` in the image, owned by its user) |
| User | any non-root user (the image runs as 10001) |
| Logs | stdout |
| Secrets | the database connection string, the Steam Web API key, the encryption key and, optional, IGDB's client id and secret, each from the environment or a mounted file; none is needed to start |

## Configuration

Everything is read from the environment. Nothing is required to start.

| Variable | Default | Meaning |
| --- | --- | --- |
| `RESPAWN_DATABASE_URL` | none | PostgreSQL connection string: a URI (`postgresql://user:password@host:5432/database`) or Npgsql's `Host=...;Username=...` form |
| `RESPAWN_DATABASE_URL_FILE` | `/secrets/database-url` | File holding the connection string, read when the variable is not set; its password is read again for every new connection |
| `RESPAWN_DATA_DIR` | none | The directory where the instance keeps its files, today the games' covers: an absolute path the app can write to, on a volume. Set it: without it nothing is kept, and every cover is fetched from Steam each time it is shown. The image has `/data` for it, and `docker-compose.yml` sets it. [The data directory](#the-data-directory) says how to set it and what goes there |
| `RESPAWN_PUBLIC_URL` | none | The address the instance is reached at, scheme and host (`https://respawn.example.org`). Set it behind a reverse proxy: linking a Steam account and signing in through a provider send the browser to Steam or the provider, which send it back to this address. Without it, the address of the request is used |
| `RESPAWN_STEAM_API_KEY` | none | The instance's [Steam Web API key](https://steamcommunity.com/dev/apikey), which reads the libraries of the linked Steam accounts. A user may give their own key on their linked account, which is then the only one that reads it. Without either, no Steam library is read |
| `RESPAWN_STEAM_API_KEY_FILE` | `/secrets/steam-api-key` | File holding the key, read when the variable is not set, and read again at each reading |
| `RESPAWN_IGDB_CLIENT_ID`, `RESPAWN_IGDB_CLIENT_SECRET` | none | The client id and secret of a [Twitch application](https://dev.twitch.tv/console/apps), the instance's access to [IGDB](https://api-docs.igdb.com/), the database of games that will bring genres, descriptions, durations and covers. Optional: without both, IGDB is never asked anything, and the start says so |
| `RESPAWN_IGDB_CLIENT_ID_FILE`, `RESPAWN_IGDB_CLIENT_SECRET_FILE` | `/secrets/igdb-client-id`, `/secrets/igdb-client-secret` | Files holding them, read when the variables are not set, and read again each time IGDB is asked |
| `RESPAWN_IGDB_GAMES_PER_TURN` | `100` | The most games IGDB is asked about every five minutes: a library seen for the first time is spread over several turns |
| `RESPAWN_ENCRYPTION_KEY` | none | 32 random bytes in base64 (`openssl rand -base64 32`; a placeholder such as all zeros is refused), which encrypt the Steam Web API keys users give on their own linked accounts. Without it, nobody can give one. Changed or lost, the keys already given can no longer be read: their syncs fail until each user gives their key again |
| `RESPAWN_ENCRYPTION_KEY_FILE` | `/secrets/encryption-key` | File holding the encryption key, read when the variable is not set, and read again each time it is needed |
| `RESPAWN_COOKIE_SECURE` | `true` | Session cookie sent over HTTPS only; `false` for plain HTTP on localhost |
| `RESPAWN_SESSION_DAYS` | `30` | Lifetime of a sign-in |
| `RESPAWN_PASSWORD_MIN_LENGTH` | `12` | Minimum length of a local password |
| `RESPAWN_LOCKOUT_THRESHOLD`, `RESPAWN_LOCKOUT_MINUTES` | `5`, `15` | Failed sign-ins before an account is locked, and for how long |
| `RESPAWN_AUTH_RATE_LIMIT` | `10/minute` | Requests per client on the sign-in routes, and apart on the returns from a sign-in provider, on the returns from Steam when an account is linked, and on the routes that give or take away a user's own Steam key (`second`, `minute` or `hour`). A client is an IPv4 address or an IPv6 /64 |
| `RESPAWN_OIDC_PROVIDERS` | none | JSON list of `{"name", "issuer", "client_id"}`, with an optional `"label"` (what the sign-in page shows, 40 characters at most) and `"create_accounts": true` (someone the provider proves gets a member account even while registration is closed): OpenID Connect providers to sign in with, the instance being a public client of each (no secret). A name is lower case letters, digits and dashes, once per list; an issuer is an `https` URL. Each provider sends the browser back to `<public address>/api/v1/auth/oidc/<name>/callback`, the address to register with it |
| `RESPAWN_TRUSTED_PROXIES` | `127.0.0.1,::1` | Comma-separated addresses or networks (`192.0.2.10`, `198.51.100.0/24`) of the reverse proxies whose `X-Forwarded-For` is trusted. Set it behind a reverse proxy |

Numbers are whole numbers of 1 or more. `RESPAWN_COOKIE_SECURE` is `true` or `false` (`1`, `yes`, `on` and
`0`, `no`, `off` are read too). A value that cannot be read is refused by name, not replaced by its default: the app does not start, and its log says which.

A URI connection string accepts the options `sslmode`, `sslrootcert`, `connect_timeout` and `application_name`;
any other is refused, so a typo is not silently ignored. Without `connect_timeout`, a connection attempt is given
up after five seconds.

### The data directory

`RESPAWN_DATA_DIR` is the only place, besides `/tmp`, where the app writes. Today it holds the covers: each
one is fetched from Steam the first time it is shown, written there, and served from that file afterwards.
The library then loads its covers from the instance itself, and a cover is still shown when Steam does not
answer or no longer has it.

How to set it:

- **With the image**: mount a volume on `/data` and set `RESPAWN_DATA_DIR=/data`, as in
  [Run it](#run-it) and in `docker-compose.yml`. `/data` belongs to the image's user (10001), so a named
  volume can be written as it is.
- **With a folder of the host** (a bind mount): make it writable by the user the container runs as
  (`chown 10001:10001 <folder>` for the image's default user).
- **From the sources**: any absolute path the app's user can write to.

What the app does with it:

- **At start**, it creates `images/` there and writes and removes a file to be sure it can. A path that is
  not absolute, or a directory it cannot write to, stops the start, and the log says why. The log then says
  once whether covers are kept, and where.
- **The files** are `images/cover/steam/<key>.<jpg|png|webp>`, one per cover. Each is written in
  `images/tmp` and then moved into place, so a stop never leaves half a file; what is left in `images/tmp` is
  removed at the next start. A cover is checked against Steam again once Steam's store has said again where
  it is (every 30 days); a changed cover is a new file, and the old one is removed once no game shows it.
- **Losing it** loses nothing else: the database holds everything the app knows, and the covers are fetched
  again as they are asked for. A backup is optional.
- **Without it**, nothing is kept: each cover is fetched from Steam at each request, and the browser keeps
  it for a week.

### The database, when it is not there

The app starts, and `GET /healthz` and the front answer, before the database is reachable; the routes that need
it answer 503 (`database_unavailable`) until it is. The schema is migrated on the first connection; if a
migration fails, the routes keep answering 503, the log holds the error and the migration is tried again a
minute later.

The connection string is read again for every new connection to the database, and its password is used: a
rotated password is picked up without a restart. Another host, database or user in it is not followed while
the app runs (the log says so): restart the app, which migrates the new database before using it.

## Behind a reverse proxy

Three things depend on where the app stands behind the proxy, and none is guessed:

- **The client's address**, which the sign-in rate limit counts by. The app sees the proxy's address unless the
  proxy is listed in `RESPAWN_TRUSTED_PROXIES`; without it every visitor shares one count, and ten sign-in requests
  a minute lock everybody out. List every proxy on the way, by address or by network. `X-Forwarded-For` is only
  read from a listed proxy, so a visitor cannot choose their own address. The app logs one warning when a request arrives
  with that header from an address that is not listed.
- **Where a state-changing request comes from**: it must come from the site's own pages. Browsers say so
  themselves in the `Sec-Fetch-Site` header, which needs nothing from the proxy. Only a browser too old to send
  it is judged on its `Origin`, compared with the `Host` header: for those, the proxy must pass the original
  `Host` on (nginx: `proxy_set_header Host $host`; Traefik and Caddy do it by default), or their sign-in is
  refused as `cross_site_request`. `X-Forwarded-Host` is not read.
- **The address Steam or a provider sends the browser back to** when a Steam account is linked or someone
  signs in through a provider: set `RESPAWN_PUBLIC_URL` to the address visitors use. Without it they are told
  the address the app itself sees. A link or a sign-in only completes
  through that address: started from another one (the proxy bypassed on the local network), it fails. A
  link in progress does not survive a restart of the app, and is simply started again; for the same reason
  the app runs as one replica.

A header such as `Remote-User` never identifies anyone: who is asking comes from the session cookie only, even
behind a proxy that signs users in.

## Sign-in

**Local accounts.** An e-mail address, a display name and a password of at least
`RESPAWN_PASSWORD_MIN_LENGTH` characters; length is the only password rule. The first account is the
administrator, and registration is closed after it. Passwords are stored as Argon2id hashes, two computed at a time at most: each takes tens of megabytes. After
`RESPAWN_LOCKOUT_THRESHOLD` failed sign-ins an account is locked for `RESPAWN_LOCKOUT_MINUTES`; a locked
account, a wrong password and an unknown e-mail get the same answer.

**OpenID Connect.** Optional: each configured provider is a button of the sign-in screen, and a link in the
account menu. The API runs the exchange itself, with ASP.NET Core's OpenID Connect handler: the browser goes to
the provider and comes back to the API (the authorization code flow with PKCE, a state and a nonce), which
redeems the code, checks the ID token (signature, issuer, audience, lifetime, nonce) and opens the session; no
token of the provider reaches the browser. Any provider with a discovery document works. An external identity
is its issuer and subject, never its e-mail: an account is not attached to a provider because the addresses
match, its owner signs in first and then links the provider from the account menu, a route of its own: a
sign-in never links, whatever session the browser carries. A new identity gets an account where registration
is open, or where its provider has `create_accounts`; that account takes the e-mail of the token only when the
provider says it is verified (`email_verified`) and it is an address. A session the browser carried when
someone signs in through a provider is closed, not only replaced.

**Sessions.** A sign-in is an opaque token in an `HttpOnly`, `SameSite=Lax` cookie, valid for
`RESPAWN_SESSION_DAYS`; only its SHA-256 is stored, and signing out deletes it.

## API

Everything is under `/api/v1`, described by `GET /api/v1/openapi.json`. The front uses only these routes.
Bodies are JSON, with names in `snake_case`.

`/docs` draws that description in a browser, with Swagger UI, and can try a route with the session of the
browser. The page is open, like the description it reads; it loads nothing from another origin, and it is not
part of the API.

| Route | Does |
| --- | --- |
| `GET /api/v1/config` | What the sign-in page may offer: local sign-in, open registration, whether the account created now is the first one, the minimum length of a password, the providers |
| `POST /api/v1/auth/register` | Creates a local account and signs in |
| `POST /api/v1/auth/login` | Signs in with a local account |
| `GET /api/v1/auth/oidc/{provider}` | Signs in through a configured provider, never links (a session the browser carries is closed and replaced). A navigation from the app's pages, not a call: the browser is sent to the provider, then comes back on `return_to` (a path of the app, the home by default), with the outcome in the `provider_sign_in` parameter unless a session was opened: `registration_closed`, `link_provider_first`, `denied`, `failed` or `unavailable`. It always answers a redirect, never an error: a start from another site's page, an unknown provider or too many attempts come back as `failed` |
| `GET /api/v1/auth/oidc/{provider}/link` | Links a configured provider to the signed-in account, the same navigation; the account must still be signed in when the browser comes back. The outcome: `linked`, `linked_elsewhere`, `denied`, `failed` (also when nobody is signed in) or `unavailable` |
| `POST /api/v1/auth/logout` | Signs out of this browser |
| `GET /api/v1/me` | The signed-in account, with the settings of its interface (`settings`: `language`, `theme`, `character`; null while it never chose) |
| `GET /api/v1/me/providers` | The providers of `/api/v1/config`, in the same order, each with `linked`: whether the signed-in account already signs in with it |
| `PUT /api/v1/me/settings` | Keeps the three settings of the interface on the signed-in account, together, in place of the ones it had |
| `GET /api/v1/linked-accounts` | The store accounts linked to the signed-in account, the earliest link first: whether a key of the user's own was given (`has_own_key`, never the key), and where its sync stands (`sync`: `running`, the `last` sync that ended, and `last_succeeded_at`, when a sync last recorded a library) |
| `GET /api/v1/linked-accounts/steam/link` | Links a Steam account. A navigation from the app's pages, not a call: the browser is sent to Steam, then comes back on `return_to` (a path of the app, the home by default) with the outcome in the `steam_link` parameter: `linked`, `linked_elsewhere`, `failed` or `unavailable` |
| `POST /api/v1/linked-accounts/{id}/sync` | Reads a linked account's library from its store now, and answers with the sync once it has ended: its `outcome` (`succeeded`, `nothing_visible`, `failed`) and, when it failed, its `error` (`not_configured`, `refused`, `unavailable`, `unreadable`). A store that cannot be read is not an error of the request. Once a minute per linked account (429 with `Retry-After`); 409 `sync_in_progress` while that account is being read |
| `DELETE /api/v1/linked-accounts/{id}` | Unlinks a store account and forgets everything that was read from it |
| `PUT /api/v1/linked-accounts/{id}/key` | Gives the user's own key for that store (`{"key": "..."}`): from then on the only key that reads the account, with no fallback to the instance's; stored encrypted, never answered. 400 `invalid_key` when it is not in the shape of the store's keys, 503 `keys_unavailable` when the instance has no encryption key. Rate-limited with `DELETE` in a bucket of their own |
| `DELETE /api/v1/linked-accounts/{id}/key` | Takes the user's own key away: the instance's key reads the account again |
| `GET /api/v1/library` | The games the signed-in account owns, one item per game with the store entries it is owned through, the playtime each store counts, and `missing_since` on an entry its store no longer lists; `sort` (`name`, `playtime`, `last_played`), `offset` and `limit` (100 by default, 500 at most) read it in parts, `total` says how many there are |
| `GET /api/v1/library/stats` | The same library in figures: games, launched and never launched, missing (flagged on every entry), total and recent playtime, games played lately, and `tracking_since`, the earliest reading that listed one of them. What a store said was played lately stops counting once the store's window has passed since that reading (Steam: two weeks), here and in the library |
| `GET /api/v1/library/history` | What was played day by day since `tracking_since`: for each day, the games played and their `seconds`, the earliest day first. A store gives totals and no history, so a day's time is the growth of those totals between two readings in a row, dated by the later one (UTC); the total a game had at its first reading is not split over time, and days and games with nothing played are left out. `from` and `to` (`YYYY-MM-DD`, both included) ask for a part of it |
| `GET /api/v1/library/years` | What was played year by year (UTC), the earliest first: for each year its `seconds`, its `source`, the days it covers (`first_day`, `last_day`) and `whole`, whether that is all of it. A year is counted from the store's own review (`review`: Steam's Year in Review, every month of it, from 2022 on) when every linked account has one, and otherwise from the daily readings (`readings`, from the first reading on); a year before the first reading, from the reviews of the accounts that have one. A review counts every game played, owned or not; the readings count the games of the library |
| `GET /api/v1/library/months/{month}` | What was played in one month (`YYYY-MM`), game by game, the most played first, each with its `name` and its `seconds`; counted as the month's year is in `/library/years`, which `source`, `first_day` and `last_day` say. A month nothing counts (before the first reading in a year with no review, or still to come) answers with `source` null and no game |
| `DELETE /api/v1/library/games/{id}` | Takes a game its store no longer lists out of the library: every entry of it flagged with `missing_since`; an entry a store still lists stays, and the game with it. What was known of it is kept, and no sync puts it back. `404` with `not_in_library`, `409` with `still_listed` when no entry is missing |
| `PUT /api/v1/library/games/{id}` | Puts back everything the account took out of that game, whether its store lists it again or not: an entry the store does not list comes back flagged as missing. `404` with `not_removed` |
| `GET /api/v1/library/removed` | The games the account took out of its library, by name, with the entries taken out (`removed_at`, `listed_again_since`, `notice_dismissed_at`). `back_since` is the notice to show: a store lists the game again since that date, and the notice was not dismissed |
| `DELETE /api/v1/library/removed/{id}/notice` | Dismisses that notice, for good: the game stays out, and nothing its store says later raises it again; the game can still be put back. `404` with `not_removed`, `409` with `not_back` when no store lists it again |
| `GET /api/v1/games/{id}` | One game as the signed-in account sees it: its name and its `stores`, the `entries` it is owned through as in the library (playtime per linked account, `missing_since`) and their total, its `rank` among the games launched, and in `removed` what the account took out of its library of it. For any game of the catalogue: one that is not in the library has no entry and no rank. `404` with `unknown_game` |
| `GET /api/v1/games/{id}/months` | The months that game was played in, the earliest first, each with its `seconds` and its `source`, counted as in `/library/years`: the months of a year add up to the game's part of that year there. A month with nothing played is left out |
| `GET /api/v1/games/{id}/cover` | A game's cover, as an image (JPEG, PNG or WebP). With `RESPAWN_DATA_DIR` set, the instance keeps each cover it serves: fetched from Steam the first time it is asked for, then served from the file with an `ETag` (`304` to a browser that holds it), fetched again once Steam's store said again where it is, and still served when Steam does not answer or no longer has it. Without it, the cover is fetched from Steam at each request. `Cache-Control` lets the browser keep it for a week (an hour until Steam's store has said where the cover is, a few minutes after the first sync). `404` with `no_cover` when the game has none, `503` with `cover_unavailable` when Steam does not answer, `429` for a client that asks for too many. For any signed-in account and any game |

Errors always have the same shape, whatever answered:

```json
{"error": {"code": "invalid_credentials", "message": "Wrong e-mail or password, or the account is locked for a while."}}
```

| Status | Codes |
| --- | --- |
| 400 | `invalid_key` |
| 401 | `not_signed_in`, `invalid_credentials` |
| 403 | `registration_closed`, `local_sign_in_disabled`, `cross_site_request`, `forbidden` |
| 404 | `unknown_linked_account`, `unknown_game`, `no_cover`, `not_in_library`, `not_removed`, `http_error` |
| 409 | `account_exists`, `sync_in_progress`, `still_listed`, `not_back` |
| 422 | `invalid_request`, `invalid_email`, `weak_password` |
| 429 | `too_many_requests` |
| 500 | `internal_error` |
| 503 | `database_unavailable`, `keys_unavailable`, `cover_unavailable` |

`http_error` is the code of any status answered without a body of its own: an unknown route (404), and also a
method a route does not allow (405).

### The sync

The library of every linked account is read from its store once a day, by the app itself: no cron job, no
other container. A day of the sync starts at 04:00 UTC; a few minutes later each library is read, one after
the other, and an account linked during the day is read within minutes. An app that was stopped reads once
when it is back. Each reading keeps the store's answer as it came, dated, and the history of a library (what
was played, and when) is the difference between two of them: a day that was not read is not recovered.

- **A store that does not answer** is asked again an hour later. Any other failure (no key, a key refused, an
  answer that cannot be read) waits for the next day, or for `POST /api/v1/linked-accounts/{id}/sync`.
- **A profile that shows nothing** (Steam: "game details" set to private) is its own outcome,
  `nothing_visible`, not a success: the library is left as it was.
- **No sync takes a game out of a library.** A game the store no longer lists (a refund, a share that
  ended, a free weekend over, or an answer cut short that still agrees with itself: the store does not say
  which) stays, flagged: `missing_since` on its entry in `GET /api/v1/library` and in
  `GET /api/v1/games/{id}`, the date of the first full answer that left it out. The flag goes when the
  store lists the game again. Taking it out is its owner's decision
  (`DELETE /api/v1/library/games/{id}`).
- **No sync puts a game back either.** A game its owner took out stays out, whatever the store says
  later. When the store lists it again, `GET /api/v1/library/removed` says so (`back_since`), and its
  owner puts it back or dismisses that notice for good.
- Every sync is kept with its outcome, in the same transaction as what it recorded. Unlinking an account
  forgets its syncs with the rest.
- A sync asked for through the API goes on to its end when the request is given up.
- `GET /api/v1/linked-accounts` says where each account's sync stands. A profile that shows nothing does not
  move `last_succeeded_at`, so an old date there is the sign that something stopped working.

### The years before the first reading

A store that keeps its own review of a year gives the past the readings cannot: Steam's Year in Review, what
was played per month and per game, from 2022 on. The app imports it once per year and linked account, with
the key that reads the library, on a schedule of its own: every fifteen minutes it looks for the linked
accounts that miss a year, so a newly linked account gets its past within minutes of its first sync.

- **The year just over** comes in once Steam has made its review, around the year's end; until then it is
  asked again a day later. From February on, a year Steam still has no review of is kept as a year with
  nothing played, as long as the last sync read the account's library with the same key: a profile Steam
  does not show answers no review either, and is asked again.
- **A failure** (no key, a key refused, Steam not answering) waits a day too, and never touches the daily
  sync: the review comes from a call Steam does not document, which may close without notice.
- Each answer is kept as it came, like a reading. Unlinking an account forgets its reviews with the rest.
- `GET /api/v1/library/years` counts a year the readings reach from the reviews only when every linked
  account has one, a year before them from the accounts that have one, and says of each year whether it is
  whole. The home draws the whole years and names the others.
- One rule counts them all: the months of a game (`GET /api/v1/games/{id}/months`) and the games of a
  month (`GET /api/v1/library/months/{month}`) come from the review when their year does in
  `/library/years`, and from the readings otherwise.

### Where the covers are

Steam's store says where each game's cover is, apart from the sync: every five minutes the app looks for
the linked accounts whose games' covers it has not asked about lately, and asks Steam's store about them,
with no key. A newly synced library gets its covers within minutes; the answer is asked again after 30 days,
and a store that does not answer is tried again later, without touching the sync. Until the store has said,
a cover is looked for at Steam's usual address.

### What IGDB says of the games

With IGDB's client id and secret, the app asks IGDB about the catalogue's games apart from the sync: every
five minutes it takes up to `RESPAWN_IGDB_GAMES_PER_TURN` games never asked about, then the ones asked about
the longest ago, and asks which IGDB game each one is, then that game's name, page, cover, genres, themes,
summary (in English) and how long players took to finish it.

- **Once for the instance.** What IGDB says of a game is kept once, whoever owns it. Two store games IGDB
  takes for the same game share what it says of it and stay two games: nothing is merged. A store game IGDB
  knows as several is taken as the one with the smallest id.
- **Asked again** after 30 days for a game IGDB knows, after 7 for one it does not, which it may add later.
  A game IGDB names but says nothing of counts as one it does not know, and what IGDB said of a game no
  store game is any more is forgotten.
- **A failure** (IGDB refusing the instance's access or not answering) is said once in the log and tried
  again an hour later; nothing of the batch it failed on is kept, and the sync never waits for it. Games
  whose answer cannot be read three turns in a row are set aside as unknown, for the games after them to
  be asked about.
- Each answer is kept as it came, like a reading. Nothing shows this yet: the game page and the backlog in
  hours come next.

These four schedules (the sync, the yearly reviews, the covers, IGDB) live in the app's process, which is
one more reason the app runs as one replica.

A breaking change to these routes means a new version beside `/api/v1`, not a change to it.

## How it is built

.NET 11 and ASP.NET Core, PostgreSQL through EF Core, and an Angular front. The app is one process, split by
responsibility rather than by layer: each module is hexagonal, a core with its use cases and ports, and
adapters around it.

| Project | Holds |
| --- | --- |
| `Respawn.Host` | Composition only: the HTTP pipeline, one registration per module, the front's files |
| `Respawn.Persistence` | The database connection: opened on first use, the schemas migrated then, a rotated password followed |
| `Respawn.SharedKernel` | What modules and the host agree on: the error format, the database contracts, what a navigation may come back to |
| `Modules/Accounts` | Accounts, sign-in and groups: a core, its infrastructure (EF Core, ASP.NET Core Identity) and its API, which runs the sign-in through a provider |
| `Modules/Games` | The catalogue and the libraries: a game as one record whatever the store, the store accounts linked to an account, what each source answered, kept as it came, who owns and played what, the sync that reads each library once a day, the import of a store's yearly reviews, the covers, fetched when asked for and kept as files when the instance has a data directory, what IGDB says of each game, and what is read back of it all: the library, one game as its owner sees it, and the time played by year, by month of a game and by game of a month, counted by one rule; and its connectors, one project per outside source behind a port of the core (`Respawn.Games.Steam`, `Respawn.Games.Igdb`) |
| `Modules/<Name>/Respawn.<Name>.Tests` | The tests of a module, which start the real app against a real PostgreSQL |
| `Respawn.Tests`, `Respawn.Testing` | The tests of the host and of the architecture rules; what every test project shares |
| [`frontend/`](frontend/README.md) | The front: Angular 22, Tailwind 4, Transloco and Vitest, in four packages (the app, the base components and design tokens, the display settings, the API: its types, its client and each feature's queries) |

The image builds the front and the app serves it: its files at the root, and its document for every address
that is neither a file nor under `/api`, `/healthz` or `/docs`, so the front routes its own screens and a reload lands
on the same one. The document is written with a nonce of its own at each answer, which the content
security policy needs to let the front's component styles through and nothing else. Its settings (the theme: the system's, dark or light; the character of the labels; the
language) are kept on the account, and in the browser, which shows them from the first frame; a sign-in brings the account's. Its types come from `frontend/openapi.json`, a copy of the
description the API serves, which the CI compares with the build. `/foundations`, in the development
build only, shows the type styles and every base component.

A module owns one PostgreSQL schema and its migrations, and never reads another module's tables. These rules
are tests: the build fails when a project references what it should not.
[CONTRIBUTING.md](CONTRIBUTING.md) has the details and says how to add a module.

## Images and releases

The image is `ghcr.io/thomas-lg-lab/respawn`.

| Tag | Follows |
| --- | --- |
| `edge`, `sha-<commit>` | `main`: every merged change |
| `X.Y.Z`, `X.Y`, `X`, `latest` | Published releases (`latest` leaves pre-releases out) |

Merging is not releasing: a release is published by hand from its draft, whose notes are written from the
titles of the merged pull requests (the first release has no earlier one to compare with: its notes are
written by hand). Every image is built by the CI after the checks pass, then started as it
is meant to run (non-root, read-only filesystem, no database, no secret) and scanned before it is pushed.

## Development

The checks need Docker only, for the backend and for the front; [CONTRIBUTING.md](CONTRIBUTING.md) has the
commands, the layout and the rules, and [AGENTS.md](AGENTS.md) the conventions of the code.
Changes go through a pull request whose title follows [Conventional Commits](https://www.conventionalcommits.org).

## Security

Report a vulnerability privately: [SECURITY.md](SECURITY.md).

## License

Copyright (C) 2026 thomas-lg. Released under the GNU Affero General Public License, version 3 or (at your
option) any later version ([LICENSE](LICENSE)): anyone who runs a modified version as a network service must
offer its source to the users of that service.
