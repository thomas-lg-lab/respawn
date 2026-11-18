<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/logo-dark.svg">
  <img src="docs/logo-light.svg" alt="" width="72" height="72">
</picture>

# respawn

[![CI](https://github.com/thomas-lg-lab/respawn/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/thomas-lg-lab/respawn/actions/workflows/ci.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/thomas-lg-lab/respawn/badge)](https://scorecard.dev/viewer/?uri=github.com/thomas-lg-lab/respawn)
[![License: AGPL-3.0-or-later](https://img.shields.io/badge/license-AGPL--3.0--or--later-blue)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](global.json)
[![PostgreSQL 18](https://img.shields.io/badge/PostgreSQL-18-4169E1)](docker-compose.yml)
[![Angular 22](https://img.shields.io/badge/Angular-22-DD0031)](frontend/package.json)
[![Tailwind CSS 4](https://img.shields.io/badge/Tailwind%20CSS-4-06B6D4)](frontend/package.json)
[![Conventional Commits](https://img.shields.io/badge/Conventional%20Commits-1.0.0-FE5196)](https://www.conventionalcommits.org)

Self-hosted game library: stats, backlog and next-game suggestions.

**Status: early days.** Accounts and sign-in work, with the public API they need, the front's first
screens (sign in, create an account, who am I), and the image the rest will ship in. The library has its
tables, its rules and one route to read it; a Steam account can be linked through the API, and its library is
then read every day. It has no screen yet.

| | |
| --- | --- |
| Today | Local accounts (the first one is the administrator), sessions, lockout and rate limit on sign-in, a versioned API with its OpenAPI description; in the API only, sign-in through OpenID Connect providers. The front, served by the app: sign in, create an account, the account menu with its settings (language, theme, character), in French and in English |
| In place, in the API only | The library: a game as one record whatever the store, what each source answered kept as it came and dated, playtime with its history, and `GET /api/v1/library`. The Steam connector: owned games and playtime, read through the official Steam Web API with the instance's key. Linking a Steam account ("Sign in through Steam", which proves the account is yours and never signs you in), the list of linked accounts, and unlinking one, which forgets everything read from it. The sync: every linked library read once a day, and at once when its owner asks. Groups, the scope sharing will use: one default group every account joins |
| Planned | Sign-in through a provider from the sign-in screen, the library and stats screens, the backlog, more connectors, next-game suggestions |

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
  -e RESPAWN_DATABASE_URL=postgresql://respawn:change-me@db.example.org:5432/respawn \
  ghcr.io/thomas-lg-lab/respawn:edge
```

What the app expects from where it runs:

| | |
| --- | --- |
| HTTP | port 8080; `GET /healthz` answers `{"status": "ok"}` with no database and no secret |
| Database | PostgreSQL (tested on 18); the app creates and migrates its own schemas on the first connection |
| Filesystem | read-only root filesystem; nothing is written outside `/tmp` |
| User | any non-root user (the image runs as 10001) |
| Logs | stdout |
| Secrets | the database connection string and the Steam Web API key, each from the environment or a mounted file; none is needed to start |

## Configuration

Everything is read from the environment. Nothing is required to start.

| Variable | Default | Meaning |
| --- | --- | --- |
| `RESPAWN_DATABASE_URL` | none | PostgreSQL connection string: a URI (`postgresql://user:password@host:5432/database`) or Npgsql's `Host=...;Username=...` form |
| `RESPAWN_DATABASE_URL_FILE` | `/secrets/database-url` | File holding the connection string, read when the variable is not set; its password is read again for every new connection |
| `RESPAWN_PUBLIC_URL` | none | The address the instance is reached at, scheme and host (`https://respawn.example.org`). Set it behind a reverse proxy: linking a Steam account sends the browser to Steam, which sends it back to this address. Without it, the address of the request is used |
| `RESPAWN_STEAM_API_KEY` | none | The instance's [Steam Web API key](https://steamcommunity.com/dev/apikey), which reads the libraries of the linked Steam accounts. Without one, no Steam library is read |
| `RESPAWN_STEAM_API_KEY_FILE` | `/secrets/steam-api-key` | File holding the key, read when the variable is not set, and read again at each reading |
| `RESPAWN_COOKIE_SECURE` | `true` | Session cookie sent over HTTPS only; `false` for plain HTTP on localhost |
| `RESPAWN_SESSION_DAYS` | `30` | Lifetime of a sign-in |
| `RESPAWN_PASSWORD_MIN_LENGTH` | `12` | Minimum length of a local password |
| `RESPAWN_LOCKOUT_THRESHOLD`, `RESPAWN_LOCKOUT_MINUTES` | `5`, `15` | Failed sign-ins before an account is locked, and for how long |
| `RESPAWN_AUTH_RATE_LIMIT` | `10/minute` | Requests per client on the sign-in routes (`second`, `minute` or `hour`). A client is an IPv4 address or an IPv6 /64 |
| `RESPAWN_OIDC_PROVIDERS` | none | JSON list of `{"name", "issuer", "client_id"}`: OpenID Connect providers to sign in with. An issuer is an `https` URL |
| `RESPAWN_TRUSTED_PROXIES` | `127.0.0.1,::1` | Comma-separated addresses or networks (`192.0.2.10`, `198.51.100.0/24`) of the reverse proxies whose `X-Forwarded-For` is trusted. Set it behind a reverse proxy |

Numbers are whole numbers of 1 or more. `RESPAWN_COOKIE_SECURE` is `true` or `false` (`1`, `yes`, `on` and
`0`, `no`, `off` are read too). A value that cannot be read is refused by name, not replaced by its default.

A URI connection string accepts the options `sslmode`, `sslrootcert`, `connect_timeout` and `application_name`;
any other is refused, so a typo is not silently ignored. Without `connect_timeout`, a connection attempt is given
up after five seconds.

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
- **The address Steam sends the browser back to** when a Steam account is linked: set `RESPAWN_PUBLIC_URL` to
  the address visitors use. Without it Steam is told the address the app itself sees. A link only completes
  through that address: started from another one (the proxy bypassed on the local network), it fails. A
  link in progress does not survive a restart of the app, and is simply started again; for the same reason
  the app runs as one replica.

A header such as `Remote-User` never identifies anyone: who is asking comes from the session cookie only, even
behind a proxy that signs users in.

## Sign-in

**Local accounts.** An e-mail address, a display name and a password of at least
`RESPAWN_PASSWORD_MIN_LENGTH` characters; length is the only password rule. The first account is the
administrator, and registration is closed after it. Passwords are stored as Argon2id hashes. After
`RESPAWN_LOCKOUT_THRESHOLD` failed sign-ins an account is locked for `RESPAWN_LOCKOUT_MINUTES`; a locked
account, a wrong password and an unknown e-mail get the same answer.

**OpenID Connect.** Optional, and in the API only for now: the sign-in screen does not offer the providers
yet. With a provider configured, a client obtains an ID token (public client, PKCE) and posts it to
`POST /api/v1/auth/oidc`; any provider with a discovery document works. The token's signature,
issuer, audience, lifetime and nonce are checked, and a token is exchanged once. An external identity is its
issuer and subject, never its e-mail: an account is not attached to a provider because the addresses match, its
owner signs in first and then links the provider. An account created from a provider takes the e-mail of the
token only when the provider says it is verified (`email_verified`).

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
| `POST /api/v1/auth/oidc` | Signs in with an ID token of a configured provider; links it to the account already signed in |
| `POST /api/v1/auth/logout` | Signs out of this browser |
| `GET /api/v1/me` | The signed-in account |
| `GET /api/v1/linked-accounts` | The store accounts linked to the signed-in account, the earliest link first |
| `GET /api/v1/linked-accounts/steam/link` | Links a Steam account. A navigation from the app's pages, not a call: the browser is sent to Steam, then comes back on `return_to` (a path of the app, the home by default) with the outcome in the `steam_link` parameter: `linked`, `linked_elsewhere`, `failed` or `unavailable` |
| `POST /api/v1/linked-accounts/{id}/sync` | Reads a linked account's library from its store now, and answers with the sync once it has ended: its `outcome` (`succeeded`, `nothing_visible`, `failed`) and, when it failed, its `error` (`not_configured`, `refused`, `unavailable`, `unreadable`). A store that cannot be read is not an error of the request. Once a minute per linked account |
| `DELETE /api/v1/linked-accounts/{id}` | Unlinks a store account and forgets everything that was read from it |
| `GET /api/v1/library` | The games the signed-in account owns, one item per game with the store entries it is owned through and the playtime each store counts; `sort` (`name`, `playtime`, `last_played`), `offset` and `limit` (100 by default, 500 at most) read it in parts, `total` says how many there are |

Errors always have the same shape, whatever answered:

```json
{"error": {"code": "invalid_credentials", "message": "Wrong e-mail or password, or the account is locked for a while."}}
```

| Status | Codes |
| --- | --- |
| 401 | `not_signed_in`, `invalid_credentials`, `invalid_token` |
| 403 | `registration_closed`, `local_sign_in_disabled`, `cross_site_request`, `forbidden` |
| 404 | `unknown_provider`, `unknown_linked_account`, `http_error` |
| 409 | `account_exists`, `sync_in_progress` |
| 422 | `invalid_request`, `invalid_email`, `weak_password` |
| 429 | `too_many_requests` |
| 500 | `internal_error` |
| 503 | `database_unavailable`, `provider_unavailable` |

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
- **A game leaves a library** only when the daily readings of two days in a row both leave it out; it is then
  gone since the first. One answer may be cut short and still agree with itself, so one is never enough, and
  a reading asked for by hand never counts, however many times it is asked.
- Every sync is kept with its outcome. Unlinking an account forgets its syncs with the rest.

The schedule lives in the app's process, which is one more reason the app runs as one replica.

A breaking change to these routes means a new version beside `/api/v1`, not a change to it.

## How it is built

.NET 10 and ASP.NET Core, PostgreSQL through EF Core, and an Angular front. The app is one process, split by
responsibility rather than by layer: each module is hexagonal, a core with its use cases and ports, and
adapters around it.

| Project | Holds |
| --- | --- |
| `Respawn.Host` | Composition only: the HTTP pipeline, one registration per module, the front's files |
| `Respawn.Persistence` | The database connection: opened on first use, the schemas migrated then, a rotated password followed |
| `Respawn.SharedKernel` | What modules and the host agree on: the error format, the database contracts |
| `Modules/Accounts` | Accounts, sign-in and groups: a core, its infrastructure (EF Core, ASP.NET Core Identity, ID token validation) and its API |
| `Modules/Games` | The catalogue and the libraries: a game as one record whatever the store, the store accounts linked to an account, what each source answered, kept as it came, who owns and played what, and the sync that reads each library once a day |
| `Modules/<Name>/Respawn.<Name>.Tests` | The tests of a module, which start the real app against a real PostgreSQL |
| `Respawn.Tests`, `Respawn.Testing` | The tests of the host and of the architecture rules; what every test project shares |
| [`frontend/`](frontend/README.md) | The front: Angular 22, Tailwind 4, Transloco and Vitest, in four packages (the app, the base components and design tokens, the display settings, the API's types) |

The image builds the front and the app serves it: its files at the root, and its document for every address
that is neither a file nor under `/api`, `/healthz` or `/docs`, so the front routes its own screens and a reload lands
on the same one. The document is written with a nonce of its own at each answer, which the content
security policy needs to let the front's component styles through and nothing else. Its settings (the theme: the system's, dark or light; the character of the labels; the
language) are kept in the browser for now. Its types come from `frontend/openapi.json`, a copy of the
description the API serves, which the CI compares with the build. `/foundations` shows the type styles and
the base components, with no session.

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
titles of the merged pull requests. Every image is built by the CI after the checks pass, then started as it
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
