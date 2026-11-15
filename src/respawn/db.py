"""Database access. The connection is opened on first use, and the schema migrated then.

The app must start, and /healthz answer, before the database is reachable: nothing here runs at import or at
start-up. A failed attempt is retried by a later request, after a pause, so an unreachable database or a
faulty migration cannot hold every request for the length of a connection attempt.

The connection string may change while the app runs (a rotated password, another file content): it is read
again every RECHECK_SECONDS, and at once after a connection failed.
"""

from __future__ import annotations

import logging
import threading
import time
from pathlib import Path

from alembic import command
from alembic.config import Config
from sqlalchemy import Engine, create_engine
from sqlalchemy.engine import make_url
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
# How often the connection string is read again once connected.
RECHECK_SECONDS = 10


class DatabaseUnavailableError(Exception):
    """No connection string yet, or the database does not answer."""


class MigrationFailedError(DatabaseUnavailableError):
    """The database answers, but its schema could not be brought up to date."""


class Database:
    def __init__(self, settings: Settings) -> None:
        self._settings = settings
        self._lock = threading.Lock()
        self._engine: Engine | None = None
        self._sessions: sessionmaker[Session] | None = None
        self._url: str | None = None
        self._recheck_at = 0.0
        self._retry_at = 0.0
        self._failure: DatabaseUnavailableError | None = None

    def _open(self, url: str) -> Engine:
        """An engine on this connection string, connected once and with the schema up to date."""
        engine: Engine | None = None
        try:
            connect_args = {}
            if "connect_timeout" not in make_url(url).query:
                connect_args["connect_timeout"] = CONNECT_TIMEOUT_SECONDS
            engine = create_engine(url, pool_pre_ping=True, connect_args=connect_args)
            with engine.connect():
                pass
        except (SQLAlchemyError, ValueError) as exc:
            if engine is not None:
                engine.dispose()
            # The driver's message names the host and the reason, never the password.
            log.warning("database unreachable: %s", _first_line(exc))
            raise DatabaseUnavailableError("the database does not answer") from exc
        try:
            migrate(engine)
        except Exception as exc:
            engine.dispose()
            log.exception("schema migration failed, next attempt in %d s", MIGRATION_RETRY_SECONDS)
            raise MigrationFailedError("the schema migration failed") from exc
        return engine

    def _use(self, engine: Engine, url: str) -> sessionmaker[Session]:
        previous = self._engine
        self._engine = engine
        self._url = url
        self._sessions = sessionmaker(engine, expire_on_commit=False)
        self._recheck_at = time.monotonic() + RECHECK_SECONDS
        if previous is not None:
            previous.dispose()
        return self._sessions

    def _connect(self) -> sessionmaker[Session]:
        sessions = self._sessions
        if sessions is not None:
            if time.monotonic() >= self._recheck_at and self._lock.acquire(blocking=False):
                # One request at a time looks at the connection string again; the others carry on.
                try:
                    sessions = self._follow(sessions)
                finally:
                    self._lock.release()
            return sessions
        with self._lock:
            if self._sessions is not None:
                return self._sessions
            if self._failure is not None and time.monotonic() < self._retry_at:
                raise self._failure
            url = self._settings.resolve_database_url()
            if not url:
                raise DatabaseUnavailableError("no database is configured")
            try:
                engine = self._open(url)
            except DatabaseUnavailableError as exc:
                pause = MIGRATION_RETRY_SECONDS if isinstance(exc, MigrationFailedError) else RETRY_SECONDS
                self._failure = exc
                self._retry_at = time.monotonic() + pause
                raise
            self._failure = None
            log.info("database ready, schema up to date")
            return self._use(engine, url)

    def _follow(self, sessions: sessionmaker[Session]) -> sessionmaker[Session]:
        """Move to the connection string now configured, when it differs from the one in use and works."""
        self._recheck_at = time.monotonic() + RECHECK_SECONDS
        url = self._settings.resolve_database_url()
        if not url or url == self._url:
            return sessions
        try:
            engine = self._open(url)
        except DatabaseUnavailableError:
            # The current connections may still work (a password changed in the file before the database).
            return sessions
        log.info("connection string changed, database reconnected")
        return self._use(engine, url)

    def connection_failed(self) -> None:
        """A request lost its connection: the next one reads the connection string again."""
        self._recheck_at = 0.0

    def session(self) -> Session:
        return self._connect()()

    def dispose(self) -> None:
        with self._lock:
            if self._engine is not None:
                self._engine.dispose()
            self._engine = None
            self._sessions = None
            self._url = None
            self._failure = None


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
