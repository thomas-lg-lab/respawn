"""Fixtures. The tests run against a real PostgreSQL, in a database of their own.

RESPAWN_TEST_DATABASE_URL names it; by default it is `respawn_test` on the compose server, beside the database
the compose app uses and never that one. It is created when missing. The tests empty its tables, so a database
whose name does not end in `_test` is refused.
"""

from __future__ import annotations

import os
import time
from collections.abc import Iterator
from pathlib import Path

import httpx
import pytest
from fastapi.testclient import TestClient
from joserfc import jwt
from joserfc.jwk import RSAKey
from sqlalchemy import create_engine, text
from sqlalchemy.engine import URL, make_url

from respawn.api import limiter
from respawn.config import OidcProvider, Settings
from respawn.main import create_app

DATABASE_URL = os.environ.get(
    "RESPAWN_TEST_DATABASE_URL", "postgresql+psycopg://respawn:respawn@localhost:5432/respawn_test"
)
ISSUER = "https://issuer.test"
CLIENT_ID = "respawn-test"
PROVIDER = OidcProvider(name="test", issuer=ISSUER, client_id=CLIENT_ID)
PASSWORD = "correct horse battery"


class FakeIssuer:
    """An OpenID Connect provider answering from memory: discovery, keys, and tokens signed on demand."""

    def __init__(self) -> None:
        self.key = RSAKey.generate_key(2048, parameters={"kid": "test-key"})
        self.other_key = RSAKey.generate_key(2048, parameters={"kid": "test-key"})
        self.requests = 0

    def rotate(self) -> None:
        """Sign with a new key from now on, as a provider does from time to time."""
        self.key = RSAKey.generate_key(2048, parameters={"kid": f"test-key-{time.monotonic_ns()}"})

    def transport(self) -> httpx.MockTransport:
        def handler(request: httpx.Request) -> httpx.Response:
            self.requests += 1
            if request.url.path == "/.well-known/openid-configuration":
                return httpx.Response(200, json={"issuer": ISSUER, "jwks_uri": f"{ISSUER}/jwks"})
            if request.url.path == "/jwks":
                return httpx.Response(200, json={"keys": [self.key.as_dict(private=False)]})
            return httpx.Response(404)

        return httpx.MockTransport(handler)

    def token(self, *, key: RSAKey | None = None, **overrides: object) -> str:
        now = int(time.time())
        claims: dict[str, object] = {
            "iss": ISSUER,
            "aud": CLIENT_ID,
            "sub": "subject-1",
            "nonce": "nonce-123456",
            "iat": now,
            "exp": now + 300,
            "email": "player@example.org",
            "email_verified": True,
            "name": "Player One",
        }
        claims.update(overrides)
        key = key or self.key
        return jwt.encode({"alg": "RS256", "kid": key.kid}, claims, key)


@pytest.fixture
def issuer() -> FakeIssuer:
    return FakeIssuer()


def admin_url(database_url: str) -> URL:
    """The same server and account, on the maintenance database: where databases and roles are created."""
    return make_url(database_url).set(database="postgres")


def create_database(database_url: str, owner: str | None = None) -> None:
    name = make_url(database_url).database
    if not name or not name.endswith("_test") or not name.replace("_", "").isalnum():
        raise pytest.UsageError(
            f"the tests empty their database: {name!r} is refused, its name must end in _test"
        )
    engine = create_engine(admin_url(database_url), isolation_level="AUTOCOMMIT")
    with engine.connect() as connection:
        exists = connection.scalar(text("SELECT 1 FROM pg_database WHERE datname = :name"), {"name": name})
        if not exists:
            connection.execute(text(f'CREATE DATABASE "{name}"' + (f' OWNER "{owner}"' if owner else "")))
    engine.dispose()


@pytest.fixture(scope="session", autouse=True)
def test_database() -> None:
    create_database(DATABASE_URL)


def settings(**overrides: object) -> Settings:
    values: dict[str, object] = {
        "database_url": DATABASE_URL,
        "cookie_secure": False,
        "lockout_threshold": 3,
        "oidc_providers": (PROVIDER,),
    }
    values.update(overrides)
    return Settings(**values)  # type: ignore[arg-type]


@pytest.fixture
def client(issuer: FakeIssuer) -> Iterator[TestClient]:
    os.environ["RESPAWN_AUTH_RATE_LIMIT"] = "1000/minute"
    limiter.reset()
    app = create_app(settings(), oidc_transport=issuer.transport())
    with TestClient(app) as test_client:
        # The first request migrates the schema; the tables are then emptied for the test.
        assert test_client.get("/api/v1/config").status_code == 200
        engine = create_engine(DATABASE_URL)
        with engine.begin() as connection:
            connection.execute(
                text("TRUNCATE users, sessions, external_identities, exchanged_tokens, settings CASCADE")
            )
        engine.dispose()
        yield test_client
    app.state.database.dispose()


@pytest.fixture
def offline_client(tmp_path: Path) -> Iterator[TestClient]:
    """The app as it starts with an empty secrets directory and no database."""
    app = create_app(Settings(database_url=None, database_url_file=tmp_path / "database-url"))
    with TestClient(app) as test_client:
        yield test_client


def register(client: TestClient, email: str = "first@example.org", name: str = "First") -> httpx.Response:
    return client.post(
        "/api/v1/auth/register", json={"email": email, "display_name": name, "password": PASSWORD}
    )
