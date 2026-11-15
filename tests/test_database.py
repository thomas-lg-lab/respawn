"""The database layer when things go wrong: unreachable, a failed migration, a changed connection string."""

from __future__ import annotations

import inspect
import time
from collections.abc import Iterator
from pathlib import Path

import pytest
from fastapi.testclient import TestClient
from sqlalchemy import create_engine, text
from sqlalchemy.engine import Engine, make_url

from respawn import db
from respawn.config import Settings
from respawn.main import create_app
from tests.conftest import DATABASE_URL, admin_url, create_database, settings

ROLE = "respawn_rotation_test"


@pytest.fixture
def admin() -> Iterator[Engine]:
    """The maintenance database, to create and drop what a test needs beside the test database."""
    engine = create_engine(admin_url(DATABASE_URL), isolation_level="AUTOCOMMIT")
    yield engine
    engine.dispose()


def other_database(admin: Engine, name: str, owner: str | None = None) -> str:
    """A second, empty database on the same server; its connection string."""
    with admin.connect() as connection:
        connection.execute(text(f'DROP DATABASE IF EXISTS "{name}" WITH (FORCE)'))
    url = make_url(DATABASE_URL).set(database=name)
    create_database(url.render_as_string(hide_password=False), owner)
    return url.render_as_string(hide_password=False)


def test_healthz_does_not_need_a_worker_thread() -> None:
    """Served by the event loop: threads all waiting for a slow database cannot starve it."""
    app = create_app(Settings(database_url=None))
    endpoint = next(route.endpoint for route in app.routes if getattr(route, "path", None) == "/healthz")
    assert inspect.iscoroutinefunction(endpoint)


def test_an_unreachable_database_is_given_up_quickly(
    monkeypatch: pytest.MonkeyPatch, caplog: pytest.LogCaptureFixture
) -> None:
    monkeypatch.setattr(db, "CONNECT_TIMEOUT_SECONDS", 1)
    # An address of the documentation range: nothing answers there.
    unreachable = "postgresql://respawn:not-in-the-log@192.0.2.1:5432/respawn_test"
    app = create_app(Settings(database_url=unreachable))
    with TestClient(app) as client:
        started = time.monotonic()
        first = client.get("/api/v1/config")
        waited = time.monotonic() - started
        assert (first.status_code, first.json()["error"]["code"]) == (503, "database_unavailable")
        assert waited < 4  # the connection timeout, not the minutes the network stack would wait
        # Until the next attempt is due, requests are answered at once instead of each waiting in turn.
        started = time.monotonic()
        assert client.get("/api/v1/config").status_code == 503
        assert time.monotonic() - started < 0.5
        assert client.get("/healthz").status_code == 200
    assert caplog.text.count("database unreachable") == 1
    assert "not-in-the-log" not in caplog.text


def test_a_connect_timeout_in_the_connection_string_is_kept() -> None:
    database = db.Database(Settings(database_url=DATABASE_URL + "?connect_timeout=2"))
    session = database.session()
    assert session.connection().connection.driver_connection.info.get_parameters()["connect_timeout"] == "2"
    session.close()
    database.dispose()


def test_a_failed_migration_is_logged_and_not_retried_at_once(
    monkeypatch: pytest.MonkeyPatch, caplog: pytest.LogCaptureFixture
) -> None:
    attempts = 0

    def broken(engine: Engine) -> None:
        nonlocal attempts
        attempts += 1
        raise RuntimeError("the migration says what went wrong")

    monkeypatch.setattr(db, "migrate", broken)
    app = create_app(settings())
    with TestClient(app) as client:
        for _ in range(3):
            response = client.get("/api/v1/config")
            assert (response.status_code, response.json()["error"]["code"]) == (
                503,
                "database_unavailable",
            )
        assert client.get("/healthz").status_code == 200
    assert attempts == 1
    assert "schema migration failed" in caplog.text
    assert "the migration says what went wrong" in caplog.text


def test_another_database_in_the_connection_string_is_not_followed(
    client: TestClient, admin: Engine, tmp_path: Path, caplog: pytest.LogCaptureFixture
) -> None:
    """Only credentials are taken on the fly: no request is served from a database that was not migrated."""
    other = other_database(admin, "respawn_other_test")  # empty: no table at all
    file = tmp_path / "database-url"
    file.write_text(DATABASE_URL, encoding="utf-8")
    database = db.Database(Settings(database_url=None, database_url_file=file))
    database.session().close()
    file.write_text(other, encoding="utf-8")
    # Three sessions at once: the pool has to open new connections, with the file naming the other database.
    sessions = [database.session() for _ in range(3)]
    try:
        for session in sessions:
            assert session.scalar(text("SELECT current_database()")) == make_url(DATABASE_URL).database
            assert session.scalar(text("SELECT count(*) FROM users")) == 0
    finally:
        for session in sessions:
            session.close()
        database.dispose()
    assert caplog.text.count("restart it to use the new one") == 1


def test_a_rotated_password_is_picked_up_without_a_restart(admin: Engine, tmp_path: Path) -> None:
    with admin.connect() as connection:
        connection.execute(text(f'DROP DATABASE IF EXISTS "{ROLE}" WITH (FORCE)'))
        connection.execute(text(f'DROP ROLE IF EXISTS "{ROLE}"'))
        connection.execute(text(f"CREATE ROLE \"{ROLE}\" LOGIN PASSWORD 'before-rotation'"))
    owned = make_url(other_database(admin, ROLE, owner=ROLE)).set(username=ROLE)
    file = tmp_path / "database-url"
    file.write_text(owned.set(password="before-rotation").render_as_string(hide_password=False), "utf-8")
    app = create_app(Settings(database_url=None, database_url_file=file))
    with TestClient(app) as client:
        assert client.get("/api/v1/config").status_code == 200
        # The operator rotates the password: the database first, then the mounted file; connections drop.
        with admin.connect() as connection:
            connection.execute(text(f"ALTER ROLE \"{ROLE}\" PASSWORD 'after-rotation'"))
            connection.execute(
                text("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE usename = :role"),
                {"role": ROLE},
            )
        file.write_text(owned.set(password="after-rotation").render_as_string(hide_password=False), "utf-8")
        # The pool reconnects, and a new connection reads the file again.
        assert client.get("/api/v1/config").status_code == 200
    app.state.database.dispose()
