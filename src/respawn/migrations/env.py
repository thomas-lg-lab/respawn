"""Alembic environment. Migrations run on the connection respawn.db.migrate() passes in."""

from __future__ import annotations

from alembic import context

from respawn.models import Base

connection = context.config.attributes["connection"]
context.configure(connection=connection, target_metadata=Base.metadata)
with context.begin_transaction():
    context.run_migrations()
