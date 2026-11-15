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
| `RESPAWN_DATABASE_URL_FILE` | `/secrets/database-url` | File holding the connection string, read when the variable is not set |
| `RESPAWN_COOKIE_SECURE` | `true` | Session cookie sent over HTTPS only; `false` for plain HTTP on localhost |
| `RESPAWN_SESSION_DAYS` | `30` | Lifetime of a sign-in |
| `RESPAWN_PASSWORD_MIN_LENGTH` | `12` | Minimum length of a local password |
| `RESPAWN_LOCKOUT_THRESHOLD`, `RESPAWN_LOCKOUT_MINUTES` | `5`, `15` | Failed sign-ins before an account is locked, and for how long |
| `RESPAWN_AUTH_RATE_LIMIT` | `10/minute` | Requests per client address on the sign-in routes |
| `RESPAWN_OIDC_PROVIDERS` | none | JSON list of `{"name", "issuer", "client_id"}`: OpenID Connect providers to sign in with |
| `FORWARDED_ALLOW_IPS` | `127.0.0.1` | Reverse proxies whose `X-Forwarded-*` headers are trusted |

The app starts, and `GET /healthz` answers, before the database is reachable; the other routes answer 503 until
it is. The schema is migrated on the first connection.

## Sign-in

Local accounts are always available; OpenID Connect is optional. With a provider configured, the front obtains an
ID token (public client, PKCE) and posts it to `POST /api/v1/auth/oidc`; any provider with a discovery document
works. An external identity is its issuer and subject, never its e-mail.

## API

Everything is under `/api/v1`, described by `/api/v1/openapi.json`. Errors are `{"error": {"code", "message"}}`.

## Development

See [CONTRIBUTING.md](CONTRIBUTING.md).
