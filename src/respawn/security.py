"""Password hashing and opaque tokens: pwdlib (Argon2) and the standard library."""

from __future__ import annotations

import hashlib
import secrets

from pwdlib import PasswordHash

_hasher = PasswordHash.recommended()
# Verified against when the account does not exist or has no password, so a wrong e-mail costs as much time
# as a wrong password.
_DUMMY_HASH = _hasher.hash(secrets.token_urlsafe(16))


def hash_password(password: str) -> str:
    return _hasher.hash(password)


def verify_password(password: str, password_hash: str | None) -> bool:
    if password_hash is None:
        _hasher.verify(password, _DUMMY_HASH)
        return False
    return _hasher.verify(password, password_hash)


def digest(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def new_token() -> tuple[str, str]:
    """A random token for the browser, and the digest that is stored."""
    token = secrets.token_urlsafe(32)
    return token, digest(token)
