"""The public API, version 1. The web page uses these routes and nothing else."""

from __future__ import annotations

import os
import re
import uuid
from collections.abc import Iterator
from typing import Annotated

from fastapi import APIRouter, Cookie, Depends, Request, Response
from pydantic import BaseModel, Field
from slowapi import Limiter
from slowapi.util import get_remote_address
from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from respawn import accounts
from respawn.config import Settings
from respawn.models import ExternalIdentity, User
from respawn.oidc import InvalidTokenError, OidcValidator
from respawn.security import hash_password

COOKIE = "respawn_session"
EMAIL = re.compile(r"^[^@\s]+@[^@\s]+\.[^@\s]+$")

router = APIRouter(prefix="/api/v1")
limiter = Limiter(key_func=get_remote_address)


def auth_limit() -> str:
    """Requests per client address on the sign-in routes."""
    return os.environ.get("RESPAWN_AUTH_RATE_LIMIT", "10/minute")


class ApiError(Exception):
    """An error answered as {"error": {"code", "message"}}, the one error format of the API."""

    def __init__(self, status: int, code: str, message: str) -> None:
        self.status = status
        self.code = code
        self.message = message


def get_settings(request: Request) -> Settings:
    settings: Settings = request.app.state.settings
    return settings


def get_db(request: Request) -> Iterator[Session]:
    db: Session = request.app.state.database.session()
    try:
        yield db
    finally:
        db.close()


Db = Annotated[Session, Depends(get_db)]
Config = Annotated[Settings, Depends(get_settings)]
SessionCookie = Annotated[str | None, Cookie(alias=COOKIE)]


def current_user(db: Db, token: SessionCookie = None) -> User:
    user = accounts.user_for_token(db, token)
    if user is None:
        raise ApiError(401, "not_signed_in", "Sign in to continue.")
    return user


class UserOut(BaseModel):
    id: uuid.UUID
    email: str | None
    display_name: str
    is_admin: bool


class ProviderOut(BaseModel):
    name: str
    issuer: str
    client_id: str


class ConfigOut(BaseModel):
    local_sign_in: bool
    registration_open: bool
    oidc_providers: list[ProviderOut]


class RegisterIn(BaseModel):
    email: str = Field(max_length=320)
    display_name: str = Field(min_length=1, max_length=100)
    password: str = Field(max_length=256)


class LoginIn(BaseModel):
    email: str = Field(max_length=320)
    password: str = Field(max_length=256)


class OidcIn(BaseModel):
    provider: str = Field(max_length=64)
    id_token: str = Field(max_length=16384)
    nonce: str = Field(min_length=8, max_length=256)


def _user(user: User) -> UserOut:
    return UserOut(id=user.id, email=user.email, display_name=user.display_name, is_admin=user.is_admin)


def _sign_in(response: Response, db: Session, settings: Settings, user: User) -> None:
    token = accounts.open_session(db, settings, user)
    db.commit()
    response.set_cookie(
        COOKIE,
        token,
        max_age=settings.session_days * 86400,
        httponly=True,
        secure=settings.cookie_secure,
        samesite="lax",
        path="/",
    )


@router.get("/config", summary="What the sign-in page may offer")
def read_config(db: Db, settings: Config) -> ConfigOut:
    return ConfigOut(
        local_sign_in=accounts.flag(db, accounts.LOCAL_SIGN_IN, True),
        registration_open=accounts.registration_allowed(db),
        oidc_providers=[
            ProviderOut(name=p.name, issuer=p.issuer, client_id=p.client_id) for p in settings.oidc_providers
        ],
    )


@router.post("/auth/register", status_code=201, summary="Create a local account and sign in")
@limiter.limit(auth_limit)
def register(request: Request, response: Response, body: RegisterIn, db: Db, settings: Config) -> UserOut:
    email = accounts.normalise_email(body.email)
    if not EMAIL.match(email):
        raise ApiError(422, "invalid_email", "Enter a valid e-mail address.")
    if len(body.password) < settings.password_min_length:
        raise ApiError(
            422, "weak_password", f"The password needs at least {settings.password_min_length} characters."
        )
    accounts.lock_registration(db)
    if not accounts.flag(db, accounts.LOCAL_SIGN_IN, True) or not accounts.registration_allowed(db):
        raise ApiError(403, "registration_closed", "Registration is closed on this instance.")
    try:
        user = accounts.create_user(
            db,
            email=email,
            display_name=body.display_name.strip(),
            password_hash=hash_password(body.password),
        )
    except IntegrityError as exc:
        raise ApiError(409, "account_exists", "An account already uses this e-mail address.") from exc
    _sign_in(response, db, settings, user)
    return _user(user)


@router.post("/auth/login", summary="Sign in with a local account")
@limiter.limit(auth_limit)
def login(request: Request, response: Response, body: LoginIn, db: Db, settings: Config) -> UserOut:
    if not accounts.flag(db, accounts.LOCAL_SIGN_IN, True):
        raise ApiError(403, "local_sign_in_disabled", "Local sign-in is disabled on this instance.")
    user = accounts.authenticate(db, settings, body.email, body.password)
    if user is None:
        db.commit()  # keeps the failed-attempt count
        raise ApiError(
            401, "invalid_credentials", "Wrong e-mail or password, or the account is locked for a while."
        )
    _sign_in(response, db, settings, user)
    return _user(user)


@router.post("/auth/oidc", summary="Sign in with an ID token from a configured provider")
@limiter.limit(auth_limit)
def login_oidc(
    request: Request, response: Response, body: OidcIn, db: Db, settings: Config, token: SessionCookie = None
) -> UserOut:
    provider = settings.provider(body.provider)
    if provider is None:
        raise ApiError(404, "unknown_provider", "This provider is not configured.")
    validator: OidcValidator = request.app.state.oidc
    try:
        identity = validator.validate(provider, body.id_token, body.nonce)
    except InvalidTokenError as exc:
        raise ApiError(401, "invalid_token", "The provider's token was refused.") from exc

    accounts.lock_registration(db)
    if not accounts.exchange_once(db, body.id_token, identity.expires_at):
        raise ApiError(401, "invalid_token", "The provider's token was refused.")

    known = db.scalar(
        select(ExternalIdentity).where(
            ExternalIdentity.issuer == identity.issuer, ExternalIdentity.subject == identity.subject
        )
    )
    if known is not None:
        user = db.get_one(User, known.user_id)
    else:
        signed_in = accounts.user_for_token(db, token)
        if signed_in is not None:
            user = signed_in  # links the provider to the account already signed in
        else:
            if not accounts.registration_allowed(db):
                raise ApiError(403, "registration_closed", "Registration is closed on this instance.")
            if identity.email and db.scalar(select(User).where(User.email == identity.email)) is not None:
                # Never attached by e-mail alone: the owner signs in first, then links the provider.
                raise ApiError(
                    409, "account_exists", "Sign in to your account first, then link this provider."
                )
            user = accounts.create_user(
                db,
                email=identity.email,
                display_name=(identity.name or identity.email or "Player")[:100],
                password_hash=None,
            )
        db.add(ExternalIdentity(user_id=user.id, issuer=identity.issuer, subject=identity.subject))
    _sign_in(response, db, settings, user)
    return _user(user)


@router.post("/auth/logout", status_code=204, summary="Sign out of this browser")
def logout(response: Response, db: Db, token: SessionCookie = None) -> None:
    accounts.close_session(db, token)
    db.commit()
    response.delete_cookie(COOKIE, path="/")


@router.get("/me", summary="The signed-in account")
def read_me(user: Annotated[User, Depends(current_user)]) -> UserOut:
    return _user(user)
