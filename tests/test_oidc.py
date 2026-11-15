"""Sign-in with an ID token, against a provider answering from memory."""

from __future__ import annotations

import time

import httpx
from fastapi.testclient import TestClient

from tests.conftest import FakeIssuer, register

NONCE = "nonce-123456"


def exchange(client: TestClient, token: str, nonce: str = NONCE, provider: str = "test") -> httpx.Response:
    return client.post("/api/v1/auth/oidc", json={"provider": provider, "id_token": token, "nonce": nonce})


def test_config_lists_the_providers(client: TestClient) -> None:
    providers = client.get("/api/v1/config").json()["oidc_providers"]
    assert providers == [{"name": "test", "issuer": "https://issuer.test", "client_id": "respawn-test"}]


def test_a_valid_token_creates_the_first_account(client: TestClient, issuer: FakeIssuer) -> None:
    response = exchange(client, issuer.token())
    assert response.status_code == 200
    assert response.json() == client.get("/api/v1/me").json()
    assert response.json()["is_admin"] is True
    assert response.json()["display_name"] == "Player One"


def test_the_same_identity_signs_in_to_the_same_account(client: TestClient, issuer: FakeIssuer) -> None:
    first = exchange(client, issuer.token()).json()
    client.cookies.clear()
    # Another token of the same subject, the e-mail changed meanwhile: resolved by issuer and subject.
    again = exchange(client, issuer.token(email="renamed@example.org", iat=int(time.time()) - 1))
    assert again.status_code == 200
    assert again.json()["id"] == first["id"]


def test_refused_tokens(client: TestClient, issuer: FakeIssuer) -> None:
    now = int(time.time())
    refused = {
        "another audience": issuer.token(aud="another-client"),
        "another issuer": issuer.token(iss="https://elsewhere.test"),
        "expired": issuer.token(exp=now - 600, iat=now - 900),
        "another nonce": issuer.token(nonce="nonce-other-value"),
        "no subject": issuer.token(sub=None),
        "signed by another key": issuer.token(key=issuer.other_key),
        "not a token": "not.a.token",
    }
    for reason, token in refused.items():
        response = exchange(client, token)
        assert response.status_code == 401, reason
        assert response.json()["error"]["code"] == "invalid_token", reason
    assert client.get("/api/v1/me").status_code == 401


def test_unknown_provider(client: TestClient, issuer: FakeIssuer) -> None:
    assert exchange(client, issuer.token(), provider="nope").status_code == 404


def test_a_token_is_exchanged_once(client: TestClient, issuer: FakeIssuer) -> None:
    token = issuer.token()
    assert exchange(client, token).status_code == 200
    client.cookies.clear()
    assert exchange(client, token).status_code == 401


def test_a_new_identity_is_refused_when_registration_is_closed(
    client: TestClient, issuer: FakeIssuer
) -> None:
    register(client)
    client.cookies.clear()
    response = exchange(client, issuer.token(sub="someone-else", email="other@example.org"))
    assert (response.status_code, response.json()["error"]["code"]) == (403, "registration_closed")


def test_a_signed_in_account_links_the_provider(client: TestClient, issuer: FakeIssuer) -> None:
    account = register(client).json()
    linked = exchange(client, issuer.token(email="different@example.org"))
    assert linked.status_code == 200
    assert linked.json()["id"] == account["id"]
    client.cookies.clear()
    again = exchange(client, issuer.token(iat=int(time.time()) - 1))
    assert again.json()["id"] == account["id"]
