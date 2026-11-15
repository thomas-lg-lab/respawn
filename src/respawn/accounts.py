"""Account rules: who may register, sessions, lockout. Thin on purpose."""

from __future__ import annotations

from datetime import datetime, timedelta

from sqlalchemy import delete, func, select, text
from sqlalchemy.orm import Session

from respawn.config import Settings
from respawn.models import ExchangedToken, SessionToken, Setting, User, utcnow
from respawn.oidc import LEEWAY_SECONDS
from respawn.security import digest, new_token, verify_password

LOCAL_SIGN_IN = "local_sign_in_enabled"
REGISTRATION_OPEN = "registration_open"


def flag(db: Session, key: str, default: bool) -> bool:
    row = db.get(Setting, key)
    return default if row is None else row.value == "true"


def set_flag(db: Session, key: str, value: bool) -> None:
    row = db.get(Setting, key)
    if row is None:
        db.add(Setting(key=key, value="true" if value else "false"))
    else:
        row.value = "true" if value else "false"


def has_users(db: Session) -> bool:
    return db.scalar(select(func.count()).select_from(User)) != 0


def registration_allowed(db: Session) -> bool:
    """The first account is always allowed and becomes the administrator; after it, registration is closed
    until an administrator opens it."""
    return not has_users(db) or flag(db, REGISTRATION_OPEN, False)


def lock_registration(db: Session) -> None:
    """One registration at a time until the transaction ends, so two simultaneous first registrations cannot
    both pass the check and become administrator. Call it before registration_allowed()."""
    db.execute(text("SELECT pg_advisory_xact_lock(7305)"))


def create_user(db: Session, *, email: str | None, display_name: str, password_hash: str | None) -> User:
    user = User(
        email=email, display_name=display_name, password_hash=password_hash, is_admin=not has_users(db)
    )
    db.add(user)
    db.flush()
    return user


def normalise_email(email: str) -> str:
    return email.strip().lower()


def authenticate(db: Session, settings: Settings, email: str, password: str) -> User | None:
    """The account for these credentials, or None. A locked account is refused like a wrong password."""
    user = db.scalar(select(User).where(User.email == normalise_email(email)).with_for_update())
    now = utcnow()
    if user is None:
        verify_password(password, None)
        return None
    # Verified even when the account is locked, so that a locked account is refused no faster than a wrong
    # password.
    valid = verify_password(password, user.password_hash)
    if user.locked_until is not None and user.locked_until > now:
        return None
    if not valid:
        user.failed_logins += 1
        if user.failed_logins >= settings.lockout_threshold:
            user.failed_logins = 0
            user.locked_until = now + timedelta(minutes=settings.lockout_minutes)
        return None
    user.failed_logins = 0
    user.locked_until = None
    return user


def open_session(db: Session, settings: Settings, user: User) -> str:
    token, token_hash = new_token()
    expires = utcnow() + timedelta(days=settings.session_days)
    db.add(SessionToken(user_id=user.id, token_hash=token_hash, expires_at=expires))
    db.execute(delete(SessionToken).where(SessionToken.expires_at < utcnow()))
    return token


def user_for_token(db: Session, token: str | None) -> User | None:
    if not token:
        return None
    return db.scalar(
        select(User)
        .join(SessionToken, SessionToken.user_id == User.id)
        .where(SessionToken.token_hash == digest(token), SessionToken.expires_at > utcnow())
    )


def close_session(db: Session, token: str | None) -> None:
    if token:
        db.execute(delete(SessionToken).where(SessionToken.token_hash == digest(token)))


def exchange_once(db: Session, id_token: str, expires_at: datetime) -> bool:
    """Record an ID token as used. False when it was already exchanged.

    A token is still accepted for LEEWAY_SECONDS after it expires, so its record is kept that long too.
    """
    forgotten = utcnow() - timedelta(seconds=LEEWAY_SECONDS)
    db.execute(delete(ExchangedToken).where(ExchangedToken.expires_at < forgotten))
    token_hash = digest(id_token)
    if db.get(ExchangedToken, token_hash) is not None:
        return False
    db.add(ExchangedToken(token_hash=token_hash, expires_at=expires_at))
    db.flush()
    return True
