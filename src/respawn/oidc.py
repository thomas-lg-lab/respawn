"""Validation of an OpenID Connect ID token sent by the front.

Nothing is specific to a provider: its discovery document gives the signing keys. The token's signature and
claims are checked by joserfc; only asymmetric algorithms are accepted, so a provider's public key can never
be used as a shared secret.
"""

from __future__ import annotations

import threading
import time
from dataclasses import dataclass
from datetime import UTC, datetime
from typing import Any

import httpx
from joserfc import jws, jwt
from joserfc.errors import JoseError
from joserfc.jwk import KeySet
from joserfc.jwt import JWTClaimsRegistry

from respawn.config import OidcProvider

ALGORITHMS = ["RS256", "PS256", "ES256"]
CACHE_SECONDS = 3600
# A provider's keys are fetched again before the cache expires only for a token naming a key that is not in it
# (the provider rotated its keys), and never more often than this: callers are not signed in yet, and must not
# be able to make the app send requests at will.
MIN_REFRESH_SECONDS = 60
LEEWAY_SECONDS = 60


class InvalidTokenError(Exception):
    """The token is not one this provider issued for this app, now."""


@dataclass(frozen=True)
class Identity:
    issuer: str
    subject: str
    email: str | None
    email_verified: bool
    name: str | None
    expires_at: datetime


class OidcValidator:
    def __init__(self, transport: httpx.BaseTransport | None = None) -> None:
        self._transport = transport
        self._lock = threading.Lock()
        self._keys: dict[str, tuple[float, KeySet]] = {}  # provider name -> (fetched at, keys)
        self._attempts: dict[str, float] = {}  # provider name -> last fetch, successful or not

    def _get(self, url: str) -> Any:
        with httpx.Client(transport=self._transport, timeout=5.0) as client:
            response = client.get(url)
            response.raise_for_status()
            return response.json()

    def _fetch(self, provider: OidcProvider) -> KeySet:
        discovery = self._get(f"{provider.issuer}/.well-known/openid-configuration")
        if str(discovery.get("issuer", "")).rstrip("/") != provider.issuer:
            raise InvalidTokenError("the provider's discovery document names another issuer")
        return KeySet.import_key_set(self._get(discovery["jwks_uri"]))

    def _key_set(self, provider: OidcProvider, kid: str | None) -> KeySet:
        """The provider's keys, from the cache when it is fresh and holds the key the token names."""
        with self._lock:
            now = time.monotonic()
            cached = self._keys.get(provider.name)
            fresh = cached is not None and now - cached[0] < CACHE_SECONDS
            if cached is not None and fresh and (kid is None or _has_key(cached[1], kid)):
                return cached[1]
            attempted = self._attempts.get(provider.name)
            if attempted is not None and now - attempted < MIN_REFRESH_SECONDS:
                if cached is not None and fresh:
                    return cached[1]  # fetched a moment ago: the token names a key the provider does not have
                raise InvalidTokenError("the provider's keys could not be fetched a moment ago")
            self._attempts[provider.name] = now
            key_set = self._fetch(provider)
            self._keys[provider.name] = (now, key_set)
            return key_set

    def validate(self, provider: OidcProvider, id_token: str, nonce: str) -> Identity:
        try:
            # Nothing is fetched for something that is not even shaped like a token.
            key_set = self._key_set(provider, _key_id(id_token))
            token = jwt.decode(id_token, key_set, algorithms=ALGORITHMS)
            claims = token.claims
            JWTClaimsRegistry(
                leeway=LEEWAY_SECONDS,
                # Providers disagree on the trailing slash of their issuer; both forms name the same one.
                iss={"essential": True, "values": [provider.issuer, provider.issuer + "/"]},
                aud={"essential": True, "value": provider.client_id},
                exp={"essential": True},
                sub={"essential": True},
                nonce={"essential": True, "value": nonce},
            ).validate(claims)
        except (JoseError, httpx.HTTPError, KeyError, ValueError) as exc:
            raise InvalidTokenError(type(exc).__name__) from exc

        audience = claims["aud"]
        if isinstance(audience, list) and len(audience) > 1 and claims.get("azp") != provider.client_id:
            raise InvalidTokenError("the token is meant for another client")
        email = claims.get("email")
        return Identity(
            issuer=provider.issuer,
            subject=str(claims["sub"]),
            email=str(email).strip().lower() if email else None,
            email_verified=claims.get("email_verified") is True,
            name=claims.get("name") or claims.get("preferred_username"),
            expires_at=datetime.fromtimestamp(int(claims["exp"]), UTC),
        )


def _key_id(id_token: str) -> str | None:
    """The key the token says it is signed with, read from its header before anything is verified."""
    kid = jws.extract_compact(id_token.encode("utf-8")).headers().get("kid")
    return kid if isinstance(kid, str) else None


def _has_key(key_set: KeySet, kid: str) -> bool:
    return any(key.kid == kid for key in key_set.keys)
