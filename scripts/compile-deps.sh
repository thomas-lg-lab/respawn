#!/bin/sh
# Compiles requirements.txt (runtime) and requirements-dev.txt (runtime + dev) from pyproject.toml, in the same
# Python image as the Dockerfile so the pins match what the image installs. Run it after changing a dependency
# range; Renovate updates the compiled files afterwards.
# EXCLUDE_NEWER=2026-01-31 ignores releases published after that date (the organisation waits three days before
# taking a new release).
set -eu
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
IMAGE="$(sed -n 's/^FROM \(python:[^ ]*\).*/\1/p' "$ROOT/Dockerfile" | head -1)"
docker run --rm -e UV_EXCLUDE_NEWER="${EXCLUDE_NEWER:-}" -v "$ROOT:/work" -w /work "$IMAGE" sh -c '
    [ -n "$UV_EXCLUDE_NEWER" ] || unset UV_EXCLUDE_NEWER
    pip install --quiet --disable-pip-version-check --root-user-action=ignore uv &&
    uv pip compile --quiet --no-header pyproject.toml -o requirements.txt &&
    uv pip compile --quiet --no-header --extra dev pyproject.toml -o requirements-dev.txt'
echo "requirements.txt and requirements-dev.txt compiled with $IMAGE"
