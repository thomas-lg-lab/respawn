# Contributing

The checks the CI runs, locally. They need Docker only: the tools run in the Python image of the Dockerfile,
against the database of `docker-compose.yml`.

```bash
docker compose up -d db
docker run --rm --network respawn_default -v "$PWD:/work" -w /work \
  -e RESPAWN_TEST_DATABASE_URL=postgresql+psycopg://respawn:respawn@db:5432/respawn \
  python:3.14-slim sh -c '
    pip install -q -r requirements-dev.txt &&
    ruff format --check src tests && ruff check src tests && mypy && pytest'
docker build -t respawn .
```

The tests empty the tables of the database they run against: never point `RESPAWN_TEST_DATABASE_URL` at a
database you care about.

## Dependencies

The ranges live in `pyproject.toml`; `requirements.txt` and `requirements-dev.txt` are compiled from it:

```bash
scripts/compile-deps.sh
```

Renovate keeps the compiled files current; run the script only after changing a range.

## Database changes

A change to `src/respawn/models.py` needs a migration in `src/respawn/migrations/versions/`, numbered after the
last one. Migrations run on the first connection, so they must be safe on a database that already holds data.
