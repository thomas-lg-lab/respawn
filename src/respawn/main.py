"""The application: /healthz, the public API under /api/v1, and one static page."""

from __future__ import annotations

import logging
import os
from collections.abc import Awaitable, Callable, Mapping
from pathlib import Path
from urllib.parse import urlsplit

import httpx
from fastapi import FastAPI, Request, Response
from fastapi.exceptions import RequestValidationError
from fastapi.responses import FileResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from slowapi.errors import RateLimitExceeded
from sqlalchemy.exc import OperationalError
from starlette.exceptions import HTTPException

from respawn import __version__
from respawn.api import ApiError, limiter, router
from respawn.config import Settings, load_settings
from respawn.db import Database, DatabaseUnavailableError, MigrationFailedError
from respawn.oidc import OidcValidator

STATIC = Path(__file__).parent / "static"
UNSAFE_METHODS = {"POST", "PUT", "PATCH", "DELETE"}

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s")
log = logging.getLogger("respawn")


def error(status: int, code: str, message: str) -> JSONResponse:
    return JSONResponse({"error": {"code": code, "message": message}}, status_code=status)


def same_site(headers: Mapping[str, str]) -> bool:
    """Whether a state-changing request comes from this site's own pages.

    The browser says so itself in Sec-Fetch-Site, a header a page cannot set: that answer does not depend on
    the Host header, so it holds behind any reverse proxy. A browser too old to send it is judged on its
    Origin, compared with the host the request names. A request with neither header does not come from a
    browser page. X-Forwarded-Host is not read.
    """
    fetch_site = headers.get("sec-fetch-site")
    if fetch_site:
        # "none" is a request the user made directly; "same-site" would let a sibling subdomain in.
        return fetch_site in ("same-origin", "none")
    origin = headers.get("origin")
    return not origin or urlsplit(origin).netloc == headers.get("host")


def create_app(
    settings: Settings | None = None, oidc_transport: httpx.BaseTransport | None = None
) -> FastAPI:
    app = FastAPI(
        title="respawn",
        version=__version__,
        docs_url=None,
        redoc_url=None,
        openapi_url="/api/v1/openapi.json",
    )
    app.state.settings = settings or load_settings()
    app.state.database = Database(app.state.settings)
    app.state.oidc = OidcValidator(oidc_transport)
    app.state.limiter = limiter

    warned: set[str] = set()

    def warn_once(key: str, message: str, *args: object) -> None:
        # Once per process: what triggers these comes from a request, and must not fill the log.
        if key not in warned:
            warned.add(key)
            log.warning(message, *args)

    @app.middleware("http")
    async def guard(request: Request, call_next: Callable[[Request], Awaitable[Response]]) -> Response:
        if "x-forwarded-for" in request.headers and not os.environ.get("FORWARDED_ALLOW_IPS"):
            warn_once(
                "proxy",
                "a request carries X-Forwarded-For but FORWARDED_ALLOW_IPS is not set: behind a reverse "
                "proxy, every visitor then counts as one client for the sign-in rate limit (see the README)",
            )
        # The session is a cookie: a state-changing request must come from this site's own pages.
        if request.method in UNSAFE_METHODS and not same_site(request.headers):
            warn_once(
                "origin",
                "request refused as coming from another site: Sec-Fetch-Site %r, Origin %r, Host %r "
                "(see the README if a reverse proxy rewrites the Host header)",
                request.headers.get("sec-fetch-site", "")[:200],
                request.headers.get("origin", "")[:200],
                request.headers.get("host", "")[:200],
            )
            return error(403, "cross_site_request", "This request comes from another site.")
        response = await call_next(request)
        response.headers["X-Content-Type-Options"] = "nosniff"
        response.headers["Referrer-Policy"] = "same-origin"
        response.headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'"
        return response

    @app.exception_handler(ApiError)
    async def api_error(_: Request, exc: ApiError) -> JSONResponse:
        return error(exc.status, exc.code, exc.message)

    @app.exception_handler(DatabaseUnavailableError)
    async def database_unavailable(_: Request, exc: Exception) -> JSONResponse:
        return error(503, "database_unavailable", "The database is not available yet.")

    @app.exception_handler(OperationalError)
    async def connection_lost(request: Request, exc: Exception) -> JSONResponse:
        # The password may have been rotated: the next request reads the connection string again.
        request.app.state.database.connection_failed()
        return error(503, "database_unavailable", "The database is not available yet.")

    @app.exception_handler(MigrationFailedError)
    async def migration_failed(_: Request, exc: Exception) -> JSONResponse:
        return error(
            503, "schema_migration_failed", "The database schema could not be updated; see the server log."
        )

    @app.exception_handler(RateLimitExceeded)
    async def rate_limited(_: Request, exc: RateLimitExceeded) -> JSONResponse:
        return error(429, "too_many_requests", "Too many attempts. Wait a moment and try again.")

    @app.exception_handler(RequestValidationError)
    async def invalid_request(_: Request, exc: RequestValidationError) -> JSONResponse:
        fields = ", ".join(sorted({str(e["loc"][-1]) for e in exc.errors()}))
        return error(422, "invalid_request", f"Check these fields: {fields}.")

    @app.exception_handler(HTTPException)
    async def http_error(_: Request, exc: HTTPException) -> JSONResponse:
        return error(exc.status_code, "http_error", str(exc.detail))

    @app.get("/healthz", include_in_schema=False)
    async def healthz() -> dict[str, str]:
        # Answers without the database or any secret: it says the process is up, nothing more. Async, so it is
        # served by the event loop and never waits for a worker thread held by a slow database.
        return {"status": "ok"}

    @app.get("/", include_in_schema=False)
    def index() -> FileResponse:
        return FileResponse(STATIC / "index.html")

    app.include_router(router)
    app.mount("/static", StaticFiles(directory=STATIC), name="static")
    return app


app = create_app()
