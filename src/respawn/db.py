"""Database access. The connection is opened on first use, and the schema migrated then.

The app must start, and /healthz answer, before the database is reachable: nothing here runs at import or at
start-up. A failed attempt is retried on the next request.
"""

from __future__ import annotations

import logging
import threading
from pathlib import Path

from alembic import command
from alembic.config import Config
from sqlalchemy import Engine, create_engine
from sqlalchemy.orm import Session, sessionmaker

from respawn.config import Settings

log = logging.getLogger("respawn.db")

MIGRATIONS = Path(__file__).parent / "migrations"


class DatabaseUnavailableError(Exception):
    """No connection string yet, or the database does not answer."""


class Database:
    def __init__(self, settings: Settings) -> None:
        self._settings = settings
        self._lock = threading.Lock()
        self._engine: Engine | None = None
        self._sessions: sessionmaker[Session] | None = None

    def _connect(self) -> sessionmaker[Session]:
        if self._sessions is not None:
            return self._sessions
        with self._lock:
            if self._sessions is not None:
                return self._sessions
            url = self._settings.resolve_database_url()
            if not url:
                raise DatabaseUnavailableError("no database is configured")
            engine = create_engine(url, pool_pre_ping=True)
            try:
                migrate(engine)
            except Exception as exc:
                engine.dispose()
                log.warning("database not ready: %s", type(exc).__name__)
                raise DatabaseUnavailableError("the database does not answer") from exc
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


def migrate(engine: Engine) -> None:
    """Bring the schema to the latest revision. Safe to run at every start."""
    config = Config()
    config.set_main_option("script_location", str(MIGRATIONS))
    with engine.begin() as connection:
        config.attributes["connection"] = connection
        command.upgrade(config, "head")
