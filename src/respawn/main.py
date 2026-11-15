"""The application: /healthz, the public API under /api/v1, and one static page."""

from __future__ import annotations

import logging
from collections.abc import Awaitable, Callable
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
from respawn.db import Database, DatabaseUnavailableError
from respawn.oidc import OidcValidator

STATIC = Path(__file__).parent / "static"
UNSAFE_METHODS = {"POST", "PUT", "PATCH", "DELETE"}

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s %(message)s")


def error(status: int, code: str, message: str) -> JSONResponse:
    return JSONResponse({"error": {"code": code, "message": message}}, status_code=status)


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

    @app.middleware("http")
    async def guard(request: Request, call_next: Callable[[Request], Awaitable[Response]]) -> Response:
        # The session is a cookie: a state-changing request must come from this site's own pages.
        origin = request.headers.get("origin")
        if (
            request.method in UNSAFE_METHODS
            and origin
            and urlsplit(origin).netloc != request.headers.get("host")
        ):
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
    @app.exception_handler(OperationalError)
    async def database_unavailable(_: Request, exc: Exception) -> JSONResponse:
        return error(503, "database_unavailable", "The database is not available yet.")

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
    def healthz() -> dict[str, str]:
        # Answers without the database or any secret: it says the process is up, nothing more.
        return {"status": "ok"}

    @app.get("/", include_in_schema=False)
    def index() -> FileResponse:
        return FileResponse(STATIC / "index.html")

    app.include_router(router)
    app.mount("/static", StaticFiles(directory=STATIC), name="static")
    return app


app = create_app()
