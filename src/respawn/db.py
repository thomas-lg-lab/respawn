"""Database access. The connection is opened on first use, and the schema migrated then.

The app must start, and /healthz answer, before the database is reachable: nothing here runs at import or at
start-up. A failed attempt is retried by a later request, after a pause, so an unreachable database or a
faulty migration cannot hold every request for the length of a connection attempt.

The connection string may change while the app runs (a rotated password): it is read again for every new
connection, through SQLAlchemy's do_connect event.
"""

from __future__ import annotations

import logging
import threading
import time
from pathlib import Path
from typing import Any

from alembic import command
from alembic.config import Config
from sqlalchemy import Engine, create_engine, event
from sqlalchemy.engine import Dialect, make_url
from sqlalchemy.exc import SQLAlchemyError
from sqlalchemy.orm import Session, sessionmaker

from respawn.config import Settings

log = logging.getLogger("respawn.db")

MIGRATIONS = Path(__file__).parent / "migrations"
# How long one connection attempt may take; without it an unreachable host holds the attempt for minutes.
CONNECT_TIMEOUT_SECONDS = 5
# Pause before another attempt after a failure. Requests arriving meanwhile are answered 503 at once.
RETRY_SECONDS = 5
MIGRATION_RETRY_SECONDS = 60


class DatabaseUnavailableError(Exception):
    """No connection string yet, the database does not answer, or its schema could not be migrated."""


class Database:
    def __init__(self, settings: Settings) -> None:
        self._settings = settings
        self._lock = threading.Lock()
        self._engine: Engine | None = None
        self._sessions: sessionmaker[Session] | None = None
        self._retry_at = 0.0

    def _create_engine(self, url: str) -> Engine:
        connect_args = {}
        if "connect_timeout" not in make_url(url).query:
            connect_args["connect_timeout"] = CONNECT_TIMEOUT_SECONDS
        engine = create_engine(url, pool_pre_ping=True, connect_args=connect_args)

        @event.listens_for(engine, "do_connect")
        def current_credentials(
            dialect: Dialect, record: Any, cargs: list[Any], cparams: dict[str, Any]
        ) -> None:
            # Every new connection uses the connection string as it is now, so a rotated password is picked
            # up without a restart.
            try:
                current = self._settings.resolve_database_url()
                if current and current != url:
                    cparams.update(dialect.create_connect_args(make_url(current))[1])
            except SQLAlchemyError:
                pass  # not a connection string: keep connecting with the one the engine was created with

        return engine

    def _connect(self) -> sessionmaker[Session]:
        if self._sessions is not None:
            return self._sessions
        with self._lock:
            if self._sessions is not None:
                return self._sessions
            if time.monotonic() < self._retry_at:
                raise DatabaseUnavailableError("the last attempt failed a moment ago")
            url = self._settings.resolve_database_url()
            if not url:
                raise DatabaseUnavailableError("no database is configured")
            try:
                engine = self._create_engine(url)
                with engine.connect():
                    pass
            except (SQLAlchemyError, ValueError) as exc:
                self._retry_at = time.monotonic() + RETRY_SECONDS
                # The driver's message names the host and the reason, never the password.
                log.warning("database unreachable: %s", _first_line(exc))
                raise DatabaseUnavailableError("the database does not answer") from exc
            try:
                migrate(engine)
            except Exception as exc:
                engine.dispose()
                self._retry_at = time.monotonic() + MIGRATION_RETRY_SECONDS
                log.exception("schema migration failed, next attempt in %d s", MIGRATION_RETRY_SECONDS)
                raise DatabaseUnavailableError("the schema migration failed") from exc
            self._engine = engine
            self._sessions = sessionmaker(engine, expire_on_commit=False)
            log.info("database ready, schema up to date")
            return self._sessions

    def session(self) -> Session:
        return self._connect()()

    def dispose(self) -> None:
        with self._lock:
            if self._engine is not None:
                self._engine.dispose()
            self._engine = None
            self._sessions = None
            self._retry_at = 0.0


def _first_line(exc: BaseException) -> str:
    lines = str(exc).strip().splitlines()
    return lines[0] if lines else type(exc).__name__


def migrate(engine: Engine) -> None:
    """Bring the schema to the latest revision. Safe to run at every start."""
    config = Config()
    config.set_main_option("script_location", str(MIGRATIONS))
    with engine.begin() as connection:
        config.attributes["connection"] = connection
        command.upgrade(config, "head")
