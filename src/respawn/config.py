"""Settings, read from the environment and from files under the secrets directory.

Nothing here is required to start: with no database configured the app still answers /healthz, and the data
routes answer 503 until the connection string appears.
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class OidcProvider:
    """An OpenID Connect provider the front may sign in with. Public values only: no client secret.

    The issuer is kept without its trailing slash; a token or a discovery document naming it with one is the
    same issuer (respawn.oidc compares both forms).
    """

    name: str
    issuer: str
    client_id: str


@dataclass(frozen=True)
class Settings:
    database_url: str | None = None
    database_url_file: Path = Path("/secrets/database-url")
    cookie_secure: bool = True
    session_days: int = 30
    password_min_length: int = 12
    lockout_threshold: int = 5
    lockout_minutes: int = 15
    oidc_providers: tuple[OidcProvider, ...] = ()

    def resolve_database_url(self) -> str | None:
        """The connection string, from the environment or from its file; read again on each call.

        The file may appear after the app has started, so nothing is cached here.
        """
        url = self.database_url
        if not url:
            try:
                url = self.database_url_file.read_text(encoding="utf-8").strip()
            except OSError:
                return None
        return _sqlalchemy_url(url) if url else None

    def provider(self, name: str) -> OidcProvider | None:
        return next((p for p in self.oidc_providers if p.name == name), None)


def _sqlalchemy_url(url: str) -> str:
    """Accept the plain PostgreSQL URI an operator or a managed database hands out."""
    for prefix in ("postgresql://", "postgres://"):
        if url.startswith(prefix):
            return "postgresql+psycopg://" + url[len(prefix) :]
    return url


def _flag(name: str, default: bool) -> bool:
    value = os.environ.get(name)
    return default if value is None else value.strip().lower() in ("1", "true", "yes", "on")


def _providers(raw: str | None) -> tuple[OidcProvider, ...]:
    """RESPAWN_OIDC_PROVIDERS is a JSON list of {"name", "issuer", "client_id"}."""
    if not raw:
        return ()
    return tuple(
        OidcProvider(name=str(p["name"]), issuer=str(p["issuer"]).rstrip("/"), client_id=str(p["client_id"]))
        for p in json.loads(raw)
    )


def load_settings() -> Settings:
    env = os.environ
    return Settings(
        database_url=env.get("RESPAWN_DATABASE_URL") or None,
        database_url_file=Path(env.get("RESPAWN_DATABASE_URL_FILE", "/secrets/database-url")),
        cookie_secure=_flag("RESPAWN_COOKIE_SECURE", True),
        session_days=int(env.get("RESPAWN_SESSION_DAYS", "30")),
        password_min_length=int(env.get("RESPAWN_PASSWORD_MIN_LENGTH", "12")),
        lockout_threshold=int(env.get("RESPAWN_LOCKOUT_THRESHOLD", "5")),
        lockout_minutes=int(env.get("RESPAWN_LOCKOUT_MINUTES", "15")),
        oidc_providers=_providers(env.get("RESPAWN_OIDC_PROVIDERS")),
    )
