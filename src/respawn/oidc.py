"""Validation of an OpenID Connect ID token sent by the front.

Nothing is specific to a provider: its discovery document gives the signing keys. The token's signature and
claims are checked by joserfc; only asymmetric algorithms are accepted, so a provider's public key can never
be used as a shared secret.
"""

from __future__ import annotations

import time
from dataclasses import dataclass
from datetime import UTC, datetime
from typing import Any

import httpx
from joserfc import jwt
from joserfc.errors import JoseError
from joserfc.jwk import KeySet
from joserfc.jwt import JWTClaimsRegistry

from respawn.config import OidcProvider

ALGORITHMS = ["RS256", "PS256", "ES256"]
CACHE_SECONDS = 3600
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
        self._keys: dict[str, tuple[float, KeySet]] = {}

    def _get(self, url: str) -> Any:
        with httpx.Client(transport=self._transport, timeout=5.0) as client:
            response = client.get(url)
            response.raise_for_status()
            return response.json()

    def _key_set(self, provider: OidcProvider, *, refresh: bool = False) -> KeySet:
        cached = self._keys.get(provider.name)
        if cached and not refresh and cached[0] > time.monotonic():
            return cached[1]
        discovery = self._get(f"{provider.issuer}/.well-known/openid-configuration")
        if str(discovery.get("issuer", "")).rstrip("/") != provider.issuer:
            raise InvalidTokenError("the provider's discovery document names another issuer")
        key_set = KeySet.import_key_set(self._get(discovery["jwks_uri"]))
        self._keys[provider.name] = (time.monotonic() + CACHE_SECONDS, key_set)
        return key_set

    def validate(self, provider: OidcProvider, id_token: str, nonce: str) -> Identity:
        try:
            try:
                token = jwt.decode(id_token, self._key_set(provider), algorithms=ALGORITHMS)
            except JoseError:
                # The provider may have rotated its keys since they were cached.
                token = jwt.decode(id_token, self._key_set(provider, refresh=True), algorithms=ALGORITHMS)
            claims = token.claims
            JWTClaimsRegistry(
                leeway=LEEWAY_SECONDS,
                iss={"essential": True, "value": provider.issuer},
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
