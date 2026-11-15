# respawn

[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/thomas-lg-lab/respawn/badge)](https://scorecard.dev/viewer/?uri=github.com/thomas-lg-lab/respawn)

Self-hosted game library: stats, backlog and next-game suggestions. Early days: this is the skeleton, accounts
and sign-in only.

## Run it

```bash
docker compose up --build
```

Then open http://localhost:8080. The first account created becomes the administrator; registration is closed
after it.

The image is `ghcr.io/thomas-lg-lab/respawn` (`edge` follows `main`). It listens on 8080, runs as any non-root
user with a read-only root filesystem, and needs a PostgreSQL database.

## Configuration

| Variable | Default | Meaning |
| --- | --- | --- |
| `RESPAWN_DATABASE_URL` | none | PostgreSQL connection string (`postgresql://user:password@host:5432/database`) |
| `RESPAWN_DATABASE_URL_FILE` | `/secrets/database-url` | File holding the connection string, read when the variable is not set; its user name and password are read again for every new connection |
| `RESPAWN_COOKIE_SECURE` | `true` | Session cookie sent over HTTPS only; `false` for plain HTTP on localhost |
| `RESPAWN_SESSION_DAYS` | `30` | Lifetime of a sign-in |
| `RESPAWN_PASSWORD_MIN_LENGTH` | `12` | Minimum length of a local password |
| `RESPAWN_LOCKOUT_THRESHOLD`, `RESPAWN_LOCKOUT_MINUTES` | `5`, `15` | Failed sign-ins before an account is locked, and for how long |
| `RESPAWN_AUTH_RATE_LIMIT` | `10/minute` | Requests per client address on the sign-in routes |
| `RESPAWN_OIDC_PROVIDERS` | none | JSON list of `{"name", "issuer", "client_id"}`: OpenID Connect providers to sign in with |
| `FORWARDED_ALLOW_IPS` | `127.0.0.1` | Comma-separated addresses or networks (`192.0.2.10`, `198.51.100.0/24`) of the reverse proxies whose `X-Forwarded-For` and `X-Forwarded-Proto` are trusted. Set it behind a reverse proxy |

The app starts, and `GET /healthz` answers, before the database is reachable; the other routes answer 503
(`database_unavailable`) until it is. The schema is migrated on the first connection; if a migration fails, the
routes keep answering 503, the log holds the error and the migration is tried again a minute later.

The connection string is read again for every new connection to the database, and its user name and password
are used: a rotated password is picked up without a restart. Another host or database in it is not followed
while the app runs (the log says so): restart the app, which migrates the new database before using it.

## Behind a reverse proxy

Two things depend on what the proxy tells the app, and neither is guessed:

- **The client's address**, which the sign-in rate limit counts by. The app sees the proxy's address unless the
  proxy is listed in `FORWARDED_ALLOW_IPS`; without it every visitor shares one count, and ten sign-in requests
  a minute lock everybody out. List every proxy on the way, by address or by network. `X-Forwarded-For` is only
  read from a listed proxy, so a visitor cannot choose their own address. The app logs one warning when a request arrives
  with that header from an address that is not listed.
- **Where a state-changing request comes from**: it must come from the site's own pages. Browsers say so
  themselves in the `Sec-Fetch-Site` header, which needs nothing from the proxy. Only a browser too old to send
  it is judged on its `Origin`, compared with the `Host` header: for those, the proxy must pass the original
  `Host` on (nginx: `proxy_set_header Host $host`; Traefik and Caddy do it by default), or their sign-in is
  refused as `cross_site_request`. `X-Forwarded-Host` is not read.

## Sign-in

Local accounts are always available; OpenID Connect is optional. With a provider configured, the front obtains an
ID token (public client, PKCE) and posts it to `POST /api/v1/auth/oidc`; any provider with a discovery document
works. An external identity is its issuer and subject, never its e-mail.

## API

Everything is under `/api/v1`, described by `/api/v1/openapi.json`. Errors are `{"error": {"code", "message"}}`.

## Development

See [CONTRIBUTING.md](CONTRIBUTING.md).
